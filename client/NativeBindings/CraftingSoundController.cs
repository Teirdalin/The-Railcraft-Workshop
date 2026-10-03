// Serialization-only authoring mirror of the installed Eco.Client component.
// Native Eco supplies its crafting-progress subscriptions and Wwise playback.
namespace Eco.Audio.SoundControllers
{
    public class SoundController:SubscribableBehavior
    {
        public bool StopOnDisable=true;
    }
    public class CraftingSoundController:SoundController
    {
        public string TableName="WainwrightTable";
    }
}
