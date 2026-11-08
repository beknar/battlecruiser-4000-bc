// The co-op server connection in a browser (the WebGL and WebGPU builds): the browser's own
// WebSocket. Messages are queued here and fetched by the game each frame (CoopSocket.cs).
mergeInto(LibraryManager.library, {

  $CoopSockets: [],

  CoopWsOpen__deps: ['$CoopSockets'],
  CoopWsOpen: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var s = { ws: null, inbox: [], state: 0, error: "" };
    try {
      s.ws = new WebSocket(url);
      s.ws.onopen = function () { s.state = 1; };
      s.ws.onmessage = function (e) { if (typeof e.data === "string") s.inbox.push(e.data); };
      s.ws.onerror = function () { if (!s.error) s.error = "couldn't reach the server (wrong address, not running, or this address isn't allowed)"; };
      s.ws.onclose = function (e) {
        s.state = 2;
        if (e.code !== 1000 && e.code !== 1005) s.error = e.reason ? e.reason : (s.error || ("closed (" + e.code + ")"));
      };
    } catch (err) {
      s.state = 2;
      s.error = "" + err;
    }
    CoopSockets.push(s);
    return CoopSockets.length - 1;
  },

  CoopWsState__deps: ['$CoopSockets'],
  CoopWsState: function (h) {
    var s = CoopSockets[h];
    return s ? s.state : 2;
  },

  CoopWsSend__deps: ['$CoopSockets'],
  CoopWsSend: function (h, textPtr) {
    var s = CoopSockets[h];
    if (s && s.state === 1) s.ws.send(UTF8ToString(textPtr));
  },

  CoopWsRecv__deps: ['$CoopSockets'],
  CoopWsRecv: function (h) {
    var s = CoopSockets[h];
    if (!s || s.inbox.length === 0) return 0;
    var str = s.inbox.shift();
    var n = lengthBytesUTF8(str) + 1;
    var buf = _malloc(n);
    stringToUTF8(str, buf, n);
    return buf;
  },

  CoopWsError__deps: ['$CoopSockets'],
  CoopWsError: function (h) {
    var s = CoopSockets[h];
    var str = s ? s.error : "";
    var n = lengthBytesUTF8(str) + 1;
    var buf = _malloc(n);
    stringToUTF8(str, buf, n);
    return buf;
  },

  CoopWsClose__deps: ['$CoopSockets'],
  CoopWsClose: function (h) {
    var s = CoopSockets[h];
    if (!s) return;
    if (s.ws) { try { s.ws.close(1000, "bye"); } catch (e) {} }
    s.state = 2;
  },
});
