#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <atomic>
#include <mutex>

using Microsoft::WRL::ComPtr;
static std::mutex resourceGate;
static ComPtr<ID3D11Texture2D> input;
static ComPtr<ID3D11Buffer> output;
static ComPtr<ID3D11Device> device;
static ComPtr<ID3D11ComputeShader> shader;
static ComPtr<ID3D11ShaderResourceView> sourceView;
static ComPtr<ID3D11UnorderedAccessView> resultView;
static UINT width, height;
static std::atomic<HRESULT> status(E_PENDING);

static const char source[] = R"(
Texture2D<float4> image : register(t0);
RWStructuredBuffer<uint4> partial : register(u0);
groupshared uint hits[256];
groupshared uint drag[256];
groupshared uint depth[256];
[numthreads(16, 16, 1)]
void main(uint3 position : SV_DispatchThreadID, uint3 group : SV_GroupID, uint index : SV_GroupIndex)
{
    uint width, height;
    image.GetDimensions(width, height);
    uint4 pixel = 0;
    if (position.x < width && position.y < height)
        pixel = uint4(round(saturate(image.Load(int3(position.xy, 0))) * 255.0));
    uint present = pixel.a != 0;
    hits[index] = present;
    drag[index] = present ? pixel.r : 0;
    depth[index] = present ? pixel.g : 0;
    GroupMemoryBarrierWithGroupSync();
    for (uint stride = 128; stride > 0; stride >>= 1)
    {
        if (index < stride)
        {
            hits[index] += hits[index + stride];
            drag[index] += drag[index + stride];
            depth[index] = max(depth[index], depth[index + stride]);
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if (index == 0)
        partial[group.y * ((width + 15) / 16) + group.x] = uint4(hits[0], drag[0], depth[0], 0);
}
)";

extern "C" __declspec(dllexport) HRESULT __cdecl ConfigureDragReduction(void* texture, void* buffer)
{
    std::lock_guard<std::mutex> lock(resourceGate);
    status = E_PENDING;
    if (!texture || !buffer) return E_INVALIDARG;
    auto* nextInput = static_cast<ID3D11Texture2D*>(texture);
    auto* nextOutput = static_cast<ID3D11Buffer*>(buffer);
    if (input.Get() == nextInput && output.Get() == nextOutput) return S_OK;
    ComPtr<ID3D11Device> nextDevice;
    nextInput->GetDevice(&nextDevice);
    ComPtr<ID3D11Device> bufferDevice;
    nextOutput->GetDevice(&bufferDevice);
    if (nextDevice.Get() != bufferDevice.Get()) return E_INVALIDARG;
    if (device.Get() != nextDevice.Get())
    {
        shader.Reset();
        device = nextDevice;
    }
    D3D11_TEXTURE2D_DESC description{};
    nextInput->GetDesc(&description);
    if (description.SampleDesc.Count != 1 || description.ArraySize != 1 || !description.Width || !description.Height
        || description.Width > 4096 || description.Height > 4096) return E_INVALIDARG;
    DXGI_FORMAT format;
    switch (description.Format)
    {
    case DXGI_FORMAT_R8G8B8A8_TYPELESS:
    case DXGI_FORMAT_R8G8B8A8_UNORM:
        format = DXGI_FORMAT_R8G8B8A8_UNORM;
        break;
    case DXGI_FORMAT_B8G8R8A8_TYPELESS:
    case DXGI_FORMAT_B8G8R8A8_UNORM:
        format = DXGI_FORMAT_B8G8R8A8_UNORM;
        break;
    default:
        return E_NOTIMPL;
    }
    D3D11_BUFFER_DESC bufferDescription{};
    nextOutput->GetDesc(&bufferDescription);
    UINT groups = ((description.Width + 15) / 16) * ((description.Height + 15) / 16);
    if (bufferDescription.StructureByteStride != 16 || bufferDescription.ByteWidth < groups * 16) return E_INVALIDARG;
    D3D11_SHADER_RESOURCE_VIEW_DESC srv{};
    srv.Format = format;
    srv.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srv.Texture2D.MipLevels = 1;
    ComPtr<ID3D11ShaderResourceView> nextSource;
    HRESULT result = device->CreateShaderResourceView(nextInput, &srv, &nextSource);
    if (FAILED(result)) return result;
    D3D11_UNORDERED_ACCESS_VIEW_DESC uav{};
    uav.Format = DXGI_FORMAT_UNKNOWN;
    uav.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
    uav.Buffer.NumElements = groups;
    ComPtr<ID3D11UnorderedAccessView> nextResult;
    result = device->CreateUnorderedAccessView(nextOutput, &uav, &nextResult);
    if (FAILED(result)) return result;
    input = nextInput;
    output = nextOutput;
    sourceView = nextSource;
    resultView = nextResult;
    width = description.Width;
    height = description.Height;
    return S_OK;
}

static void __stdcall Reduce(int)
{
    std::lock_guard<std::mutex> lock(resourceGate);
    if (!device || !input || !output) { status = E_UNEXPECTED; return; }
    if (!shader)
    {
        ComPtr<ID3DBlob> bytecode, errors;
        HRESULT result = D3DCompile(source, sizeof(source) - 1, "KSPCF drag statistics", nullptr, nullptr, "main", "cs_5_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &bytecode, &errors);
        if (SUCCEEDED(result)) result = device->CreateComputeShader(bytecode->GetBufferPointer(), bytecode->GetBufferSize(), nullptr, &shader);
        if (FAILED(result)) { status = result; return; }
    }
    ComPtr<ID3D11DeviceContext> context;
    device->GetImmediateContext(&context);
    ComPtr<ID3D11ComputeShader> oldShader;
    ComPtr<ID3D11ShaderResourceView> oldSource;
    ComPtr<ID3D11UnorderedAccessView> oldResult;
    ID3D11RenderTargetView* renderTargets[8]{};
    ComPtr<ID3D11DepthStencilView> depthTarget;
    ID3D11ClassInstance* instances[256]{};
    UINT instanceCount = 256;
    context->CSGetShader(&oldShader, instances, &instanceCount);
    context->CSGetShaderResources(0, 1, &oldSource);
    context->CSGetUnorderedAccessViews(0, 1, &oldResult);
    context->OMGetRenderTargets(8, renderTargets, &depthTarget);
    UINT targetCount = 8;
    while (targetCount && !renderTargets[targetCount - 1]) --targetCount;
    context->OMSetRenderTargetsAndUnorderedAccessViews(0, nullptr, nullptr, 0,
        D3D11_KEEP_UNORDERED_ACCESS_VIEWS, nullptr, nullptr);
    ID3D11ShaderResourceView* srv = sourceView.Get();
    ID3D11UnorderedAccessView* uav = resultView.Get();
    context->CSSetShader(shader.Get(), nullptr, 0);
    context->CSSetShaderResources(0, 1, &srv);
    context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);
    context->Dispatch((width + 15) / 16, (height + 15) / 16, 1);
    srv = nullptr;
    uav = nullptr;
    context->CSSetShaderResources(0, 1, &srv);
    context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);
    srv = oldSource.Get();
    uav = oldResult.Get();
    context->CSSetShaderResources(0, 1, &srv);
    context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);
    context->CSSetShader(oldShader.Get(), instances, instanceCount);
    context->OMSetRenderTargetsAndUnorderedAccessViews(targetCount, renderTargets, depthTarget.Get(), 0,
        D3D11_KEEP_UNORDERED_ACCESS_VIEWS, nullptr, nullptr);
    for (auto* target : renderTargets) if (target) target->Release();
    for (UINT index = 0; index < instanceCount; ++index) instances[index]->Release();
    status = S_OK;
}

extern "C" __declspec(dllexport) void* __cdecl GetDragReductionEvent() { return reinterpret_cast<void*>(&Reduce); }
extern "C" __declspec(dllexport) HRESULT __cdecl GetDragReductionStatus() { return status.load(); }

extern "C" __declspec(dllexport) void __cdecl ReleaseDragReduction()
{
    std::lock_guard<std::mutex> lock(resourceGate);
    resultView.Reset();
    sourceView.Reset();
    shader.Reset();
    input.Reset();
    output.Reset();
    device.Reset();
    status = E_PENDING;
}
