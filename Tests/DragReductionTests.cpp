#include "../KSPCommunityFixes/Native/DragReduction.cpp"
#include <cstdio>
#include <vector>

int main()
{
    ComPtr<ID3D11Device> testDevice;
    ComPtr<ID3D11DeviceContext> context;
    const D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_0};
    HRESULT result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, levels, 1, D3D11_SDK_VERSION, &testDevice, nullptr, &context);
    if (FAILED(result)) return 1;
    D3D11_BUFFER_DESC sentinelDescription{};
    sentinelDescription.ByteWidth = sentinelDescription.StructureByteStride = 16;
    sentinelDescription.Usage = D3D11_USAGE_DEFAULT;
    sentinelDescription.BindFlags = D3D11_BIND_UNORDERED_ACCESS;
    sentinelDescription.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
    ComPtr<ID3D11Buffer> csSentinel, omSentinel;
    if (FAILED(testDevice->CreateBuffer(&sentinelDescription, nullptr, &csSentinel))
        || FAILED(testDevice->CreateBuffer(&sentinelDescription, nullptr, &omSentinel))) return 20;
    D3D11_UNORDERED_ACCESS_VIEW_DESC counterDescription{};
    counterDescription.Format = DXGI_FORMAT_UNKNOWN;
    counterDescription.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
    counterDescription.Buffer.NumElements = 1;
    counterDescription.Buffer.Flags = D3D11_BUFFER_UAV_FLAG_COUNTER;
    ComPtr<ID3D11UnorderedAccessView> csCounter, omCounter;
    if (FAILED(testDevice->CreateUnorderedAccessView(csSentinel.Get(), &counterDescription, &csCounter))
        || FAILED(testDevice->CreateUnorderedAccessView(omSentinel.Get(), &counterDescription, &omCounter))) return 21;
    ComPtr<ID3DBlob> sentinelCode;
    const char sentinelSource[] = "[numthreads(1,1,1)] void main() {}";
    ComPtr<ID3D11ComputeShader> sentinelShader;
    if (FAILED(D3DCompile(sentinelSource, sizeof(sentinelSource)-1, nullptr, nullptr, nullptr,
        "main", "cs_5_0", 0, 0, &sentinelCode, nullptr))
        || FAILED(testDevice->CreateComputeShader(sentinelCode->GetBufferPointer(), sentinelCode->GetBufferSize(), nullptr, &sentinelShader))) return 22;
    unsigned seed = 882451;
    int checks = 0;
    const DXGI_FORMAT formats[] = {DXGI_FORMAT_R8G8B8A8_UNORM, DXGI_FORMAT_R8G8B8A8_TYPELESS, DXGI_FORMAT_B8G8R8A8_UNORM, DXGI_FORMAT_B8G8R8A8_TYPELESS};
    for (DXGI_FORMAT format : formats)
    for (const auto& size : {std::pair<UINT, UINT>{1, 1}, {15, 17}, {256, 256}, {257, 263}})
    for (int pattern = 0; pattern < 3; ++pattern)
    {
        UINT w = size.first, h = size.second, groups = ((w + 15) / 16) * ((h + 15) / 16);
        std::vector<unsigned char> pixels(size_t(w) * h * 4);
        UINT expectedHits = 0, expectedDrag = 0, expectedDepth = 0;
        bool bgra = format == DXGI_FORMAT_B8G8R8A8_UNORM || format == DXGI_FORMAT_B8G8R8A8_TYPELESS;
        for (size_t offset = 0; offset < pixels.size(); offset += 4)
        {
            for (int channel = 0; channel < 4; ++channel)
            {
                seed = seed * 1664525u + 1013904223u;
                pixels[offset + channel] = static_cast<unsigned char>(seed >> 24);
            }
            if (pattern == 0) pixels[offset + 3] = 0;
            if (pattern == 1) pixels[offset + 3] = (offset & 4) ? 1 : 255;
            if (pixels[offset + 3])
            {
                expectedHits++;
                expectedDrag += pixels[offset + (bgra ? 2 : 0)];
                expectedDepth = max(expectedDepth, UINT(pixels[offset + 1]));
            }
        }
        D3D11_TEXTURE2D_DESC textureDescription{};
        textureDescription.Width = w;
        textureDescription.Height = h;
        textureDescription.MipLevels = textureDescription.ArraySize = 1;
        textureDescription.Format = format;
        textureDescription.SampleDesc.Count = 1;
        textureDescription.Usage = D3D11_USAGE_DEFAULT;
        textureDescription.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
        D3D11_SUBRESOURCE_DATA data{pixels.data(), w * 4, 0};
        ComPtr<ID3D11Texture2D> sourceTexture;
        if (FAILED(testDevice->CreateTexture2D(&textureDescription, &data, &sourceTexture))) return 2;
        D3D11_RENDER_TARGET_VIEW_DESC rtvDescription{};
        rtvDescription.Format = bgra ? DXGI_FORMAT_B8G8R8A8_UNORM : DXGI_FORMAT_R8G8B8A8_UNORM;
        rtvDescription.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
        ComPtr<ID3D11RenderTargetView> renderView;
        if (FAILED(testDevice->CreateRenderTargetView(sourceTexture.Get(), &rtvDescription, &renderView))) return 3;
        auto* renderPointer = renderView.Get();
        ID3D11RenderTargetView* targets[3] = {nullptr, nullptr, renderPointer};
        auto* csPointer = csCounter.Get();
        auto* omPointer = omCounter.Get();
        UINT csInitial = 17, omInitial = 29;
        context->OMSetRenderTargetsAndUnorderedAccessViews(3, targets, nullptr, 3, 1, &omPointer, &omInitial);
        context->CSSetUnorderedAccessViews(0, 1, &csPointer, &csInitial);
        context->CSSetShader(sentinelShader.Get(), nullptr, 0);
        D3D11_BUFFER_DESC bufferDescription{};
        bufferDescription.ByteWidth = groups * 16;
        bufferDescription.Usage = D3D11_USAGE_DEFAULT;
        bufferDescription.BindFlags = D3D11_BIND_UNORDERED_ACCESS | D3D11_BIND_SHADER_RESOURCE;
        bufferDescription.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
        bufferDescription.StructureByteStride = 16;
        ComPtr<ID3D11Buffer> resultBuffer;
        if (FAILED(testDevice->CreateBuffer(&bufferDescription, nullptr, &resultBuffer))) return 4;
        if (FAILED(ConfigureDragReduction(sourceTexture.Get(), resultBuffer.Get()))) return 5;
        reinterpret_cast<void(__stdcall*)(int)>(GetDragReductionEvent())(0);
        if (FAILED(GetDragReductionStatus())) return 6;
        ComPtr<ID3D11RenderTargetView> restoredView;
        ID3D11RenderTargetView* restoredTargets[3]{};
        context->OMGetRenderTargets(3, restoredTargets, nullptr);
        restoredView.Attach(restoredTargets[2]);
        if (restoredTargets[0] || restoredTargets[1]) return 23;
        if (restoredView.Get() != renderView.Get()) return 7;
        ComPtr<ID3D11ShaderResourceView> restoredSource;
        ComPtr<ID3D11UnorderedAccessView> restoredOutput;
        context->CSGetShaderResources(0, 1, &restoredSource);
        context->CSGetUnorderedAccessViews(0, 1, &restoredOutput);
        if (restoredSource || restoredOutput.Get() != csCounter.Get()) return 8;
        ComPtr<ID3D11UnorderedAccessView> restoredOm;
        context->OMGetRenderTargetsAndUnorderedAccessViews(0, nullptr, nullptr, 3, 1, &restoredOm);
        if (restoredOm.Get() != omCounter.Get()) return 24;
        ComPtr<ID3D11ComputeShader> restoredShader;
        context->CSGetShader(&restoredShader, nullptr, nullptr);
        if (restoredShader.Get() != sentinelShader.Get()) return 25;
        D3D11_BUFFER_DESC countDescription{};
        countDescription.ByteWidth = 8;
        countDescription.Usage = D3D11_USAGE_DEFAULT;
        ComPtr<ID3D11Buffer> counts;
        if (FAILED(testDevice->CreateBuffer(&countDescription, nullptr, &counts))) return 26;
        context->CopyStructureCount(counts.Get(), 0, csCounter.Get());
        context->CopyStructureCount(counts.Get(), 4, omCounter.Get());
        countDescription.Usage = D3D11_USAGE_STAGING;
        countDescription.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Buffer> countStaging;
        if (FAILED(testDevice->CreateBuffer(&countDescription, nullptr, &countStaging))) return 27;
        context->CopyResource(countStaging.Get(), counts.Get());
        D3D11_MAPPED_SUBRESOURCE counterValues{};
        if (FAILED(context->Map(countStaging.Get(), 0, D3D11_MAP_READ, 0, &counterValues))) return 28;
        const auto* values = static_cast<const UINT*>(counterValues.pData);
        bool countersPreserved = values[0] == 17 && values[1] == 29;
        context->Unmap(countStaging.Get(), 0);
        if (!countersPreserved) return 29;
        bufferDescription.Usage = D3D11_USAGE_STAGING;
        bufferDescription.BindFlags = bufferDescription.MiscFlags = bufferDescription.StructureByteStride = 0;
        bufferDescription.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Buffer> staging;
        if (FAILED(testDevice->CreateBuffer(&bufferDescription, nullptr, &staging))) return 9;
        context->CopyResource(staging.Get(), resultBuffer.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (FAILED(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped))) return 10;
        const auto* partial = static_cast<const UINT*>(mapped.pData);
        UINT hits = 0, drag = 0, depth = 0;
        for (UINT group = 0; group < groups; ++group)
        {
            hits += partial[group * 4];
            drag += partial[group * 4 + 1];
            depth = max(depth, partial[group * 4 + 2]);
        }
        context->Unmap(staging.Get(), 0);
        if (hits != expectedHits || drag != expectedDrag || depth != expectedDepth)
        {
            std::printf("Mismatch: %ux%u format=%u pattern=%d expected=%u/%u/%u actual=%u/%u/%u\n", w, h, unsigned(format), pattern, expectedHits, expectedDrag, expectedDepth, hits, drag, depth);
            return 11;
        }
        checks++;
    }
    if (SUCCEEDED(ConfigureDragReduction(nullptr, nullptr))) return 12;
    ReleaseDragReduction();
    std::printf("Drag reduction: %d GPU cases passed; exact integer outputs and render-target/compute/OM UAV bindings and append counters restored.\n", checks);
    return 0;
}
