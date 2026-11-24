// The browser's speech recognition for the game's voice commands (VoiceInput), while the player
// holds push-to-talk. Nothing here calls into the game: it polls (as it does the co-op socket), so
// nothing is left pointing into the game when the page leaves.
mergeInto(LibraryManager.library, {
  // 1: speech recognition is here; 0: the browser has none (Firefox, Safari); 2: it has, but the
  // page isn't secure (http), so the microphone is out of reach
  CarrierVoiceSupported: function () {
    var R = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!R) return 0;
    return window.isSecureContext ? 1 : 2;
  },

  // listen (push-to-talk held): every final result is kept for CarrierVoicePoll
  CarrierVoiceStart: function () {
    var R = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!R) return;
    var v = window.carrierVoice || (window.carrierVoice = { heard: [], listening: false, rec: null, wanted: false });
    v.wanted = true;
    try { if (v.rec) v.rec.abort(); } catch (e) { }
    var rec = new R();
    rec.lang = "en-US";
    rec.continuous = true;
    rec.interimResults = false;
    rec.maxAlternatives = 1;
    rec.onstart = function () { v.listening = true; };
    rec.onend = function () {
      v.listening = false;
      if (v.rec === rec) v.rec = null;
      // (Chrome ends a session by itself after a silence or a minute: again, while still held)
      if (v.wanted) { try { var again = new R(); again.lang = rec.lang; again.continuous = true; again.interimResults = false;
                            again.onstart = rec.onstart; again.onend = rec.onend; again.onerror = rec.onerror; again.onresult = rec.onresult;
                            v.rec = again; rec = again; again.start(); return; } catch (e) { } }
      v.heard.push("end|0|");
    };
    rec.onerror = function (e) { v.heard.push("error|0|" + (e.error || "unknown")); };
    rec.onresult = function (e) {
      for (var i = e.resultIndex; i < e.results.length; i++)
        if (e.results[i].isFinal) v.heard.push("ok|" + (e.results[i][0].confidence || 0).toFixed(2) + "|" + e.results[i][0].transcript);
    };
    v.rec = rec;
    try { rec.start(); } catch (e) { v.heard.push("error|0|" + e.message); }
  },

  // push-to-talk let go: what was said so far is finished and comes in
  CarrierVoiceStop: function () {
    var v = window.carrierVoice;
    if (v) v.wanted = false;
    if (v && v.rec) try { v.rec.stop(); } catch (e) { }
  },

  // the next thing heard: "ok|confidence|words", "error|0|why" or "end|0|" (0: nothing)
  CarrierVoicePoll: function () {
    var v = window.carrierVoice;
    if (!v || v.heard.length === 0) return 0;
    var s = v.heard.shift();
    var n = lengthBytesUTF8(s) + 1;
    var p = _malloc(n);
    stringToUTF8(s, p, n);
    return p;
  },
});
