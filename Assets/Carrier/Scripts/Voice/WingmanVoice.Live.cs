namespace CarrierGame.Demo
{
    /// <summary>The wingman's live voice, for lines with no stock clip: none yet (they're shown,
    /// not spoken).</summary>
    public partial class WingmanVoice
    {
        bool LiveBusy => false;
        bool LiveSay(string text) => false;
        void LiveStop() { }
    }
}
