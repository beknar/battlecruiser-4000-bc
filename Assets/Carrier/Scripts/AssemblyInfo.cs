using System.Runtime.CompilerServices;

// The game's code is its own assembly (Carrier.Game) so the tests in Assets/Carrier/Tests can
// reference it; they may also use what is internal to it.
[assembly: InternalsVisibleTo("Carrier.Tests.EditMode")]
[assembly: InternalsVisibleTo("Carrier.Tests.PlayMode")]
