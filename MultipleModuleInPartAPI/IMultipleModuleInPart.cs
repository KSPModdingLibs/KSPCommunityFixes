/*
    Implement this interface on a PartModule derivative if the module can potentially be present multiple times in the same part.
    This will ensure that in an existing save or ship, when your module is loaded while the part configuration has been modified
    due to the user installing/uninstalling/updating mods, the persisted state is loaded into the right module.

    To implement that interface, the recommended way is to :
    - Apply the interface to your module : "public class MyModule : PartModule, IMultipleModuleInPart"
    - Add the following field to your module : "[KSPField(isPersistant = true)] public string modulePartConfigId;
    - Implement the ModulePartConfigId property : "public string ModulePartConfigId => modulePartConfigId;"

    Then, in the part configs, make sure to assign an unique "modulePartConfigId" to each occurence of the module :
    - It doesn't have to be unique over different parts.<br/>
    - It doesn't have to be unique over different modules implementing the interface.
    - It isn't mandatory to define it if there is only one occurrence of the module in a part. However, this is still recommended
    to future-proof your part config in case you want to add another occurrence of that module latter, or if your module is added by
    another mod through a MM patch.

    Note that implementing the KSPField isn't mandatory. If for whatever reason you don't want to do that, you must :
    - Ensure that when saving your module, the id is saved in a top-level value named "modulePartConfigId".
    - Ensure that the ModulePartConfigId property returns the correct id right after the module instantiation.

    Full documentation : https://github.com/KSPModdingLibs/KSPCommunityFixes/wiki/MultipleModuleInPartAPI
*/

namespace MultipleModuleInPartAPI
{
    public interface IMultipleModuleInPart
    {
        string ModulePartConfigId { get; }
    }
}
