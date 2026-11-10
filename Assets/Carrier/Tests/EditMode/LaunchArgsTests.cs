using NUnit.Framework;

namespace CarrierGame.Tests
{
    /// <summary>Reading the command line and the page address (LaunchArgs).</summary>
    public class LaunchArgsTests
    {
        [TearDown]
        public void Restore()
        {
            LaunchArgs.TestArgs = null;
            LaunchArgs.TestUrl = null;
        }

        static void CommandLine(params string[] args) => LaunchArgs.TestArgs = args;

        [Test]
        public void FlagsAndTheirValuesAreRead()
        {
            CommandLine("Game.exe", "-systemtest", "D:\\out", "-landworld", "Ochre_Minor", "-connect", "10.0.0.2:7777");
            Assert.That(LaunchArgs.Has("-systemtest"), Is.True);
            Assert.That(LaunchArgs.Has("-reel"), Is.False);
            Assert.That(LaunchArgs.ValueOf("-landworld"), Is.EqualTo("Ochre_Minor"));
            Assert.That(LaunchArgs.ValueOf("-connect"), Is.EqualTo("10.0.0.2:7777"));
            Assert.That(LaunchArgs.ValueOf("-name"), Is.Null);
        }

        [Test]
        public void AFlagAtTheEndHasNoValue()
        {
            CommandLine("Game.exe", "-connect");
            Assert.That(LaunchArgs.ValueOf("-connect"), Is.Null);
        }

        [Test]
        public void AFolderIsTheNextThingUnlessItIsAnotherFlag()
        {
            CommandLine("Game.exe", "-reel", "D:\\reel", "-all");
            Assert.That(LaunchArgs.FolderAfter("-reel", "fallback"), Is.EqualTo("D:\\reel"));
            CommandLine("Game.exe", "-reel", "-all");
            Assert.That(LaunchArgs.FolderAfter("-reel", "fallback"), Is.EqualTo("fallback"));
            CommandLine("Game.exe", "-autotest");
            Assert.That(LaunchArgs.FolderAfter("-autotest", "fallback"), Is.EqualTo("fallback"));
            Assert.That(LaunchArgs.FolderAfter("-reel", "fallback"), Is.EqualTo("fallback"));
        }

        [Test]
        public void ASwitchWorksFromTheCommandLineOrThePage()
        {
            CommandLine("Game.exe", "-landtest");
            Assert.That(LaunchArgs.Switch("landtest"), Is.True);
            Assert.That(LaunchArgs.Switch("minetest"), Is.False);
            CommandLine("Game.exe");
            LaunchArgs.TestUrl = "https://example.org/game/index.html?systemtest&minetest";
            Assert.That(LaunchArgs.Switch("minetest"), Is.True);
            Assert.That(LaunchArgs.Switch("landtest"), Is.False);
        }

        [Test]
        public void ThePagesOptionsAreRead()
        {
            CommandLine("Game.exe");
            LaunchArgs.TestUrl = "https://example.org/index.html?server=1.2.3.4%3A7777&name=Ana&connect";
            Assert.That(LaunchArgs.OnWeb, Is.True);
            Assert.That(LaunchArgs.PageOption("server"), Is.EqualTo("1.2.3.4:7777"));
            Assert.That(LaunchArgs.PageOption("name"), Is.EqualTo("Ana"));
            Assert.That(LaunchArgs.PageOption("connect"), Is.EqualTo(""));
            Assert.That(LaunchArgs.PageOption("landworld"), Is.Null);
        }

        [Test]
        public void OutsideABrowserThereIsNoPage()
        {
            CommandLine("Game.exe");
            Assert.That(LaunchArgs.OnWeb, Is.False);
            Assert.That(LaunchArgs.PageUrl, Is.EqualTo(""));
            Assert.That(LaunchArgs.PageHas("systemtest"), Is.False);
            Assert.That(LaunchArgs.PageOption("server"), Is.Null);
        }

        [TestCase("-systemtest", true)]
        [TestCase("-reel", true)]
        [TestCase("-autotest", true)]
        [TestCase("-cooptest", true)]
        [TestCase("-connect", false)]
        public void TestsAndReelsCountAsAutomated(string flag, bool automated)
        {
            CommandLine("Game.exe", flag);
            Assert.That(LaunchArgs.Automated, Is.EqualTo(automated));
        }

        [Test]
        public void ASelfTestPageCountsAsAutomated()
        {
            CommandLine("Game.exe");
            LaunchArgs.TestUrl = "https://example.org/index.html?padtest";
            Assert.That(LaunchArgs.Automated, Is.True);
            LaunchArgs.TestUrl = "https://example.org/index.html?server=x";
            Assert.That(LaunchArgs.Automated, Is.False);
        }
    }
}
