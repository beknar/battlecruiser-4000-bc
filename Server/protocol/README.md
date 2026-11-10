# Co-op protocol samples

One of each message the server sends, with every field it can carry (a `room` after a change of
level has the welcome's fields; one after a change of host has only `level`, `epoch` and `host`).
The names and the version are in `Assets/Carrier/Scripts/Net/CoopWire.cs`.

Both sides' tests read these:

- the server's (`Server/CoopServer.Tests`): every field of every message the hub sends is here;
- the game's (EditMode, `CoopTests`): every field here is one the game reads.

So a field added on one side and not the other fails a test before a release does.
