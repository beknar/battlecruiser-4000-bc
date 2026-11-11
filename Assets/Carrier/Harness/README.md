# The test and reel harness

The self-tests, the autotest and the demo reels. They start themselves from the command line
or the page address (`-systemtest`, `-autotest`, `-reel`, `?systemtest`, `?padtest`) and drive the
game as a player would.

They are their own assembly (`Carrier.Harness`), which references the game, so the game can't
come to depend on them. They ship in the release builds, since the web self-test and the reels
run from those.
