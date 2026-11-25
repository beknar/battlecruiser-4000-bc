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

  // listen (push-to-talk held): every final result is kept for CarrierVoicePoll, with the
  // recogniser's best few guesses (the game sends the first that reads as an order). With a
  // microphone chosen (CarrierVoiceSetMic), recognition is given that microphone's track.
  CarrierVoiceStart: function () {
    var R = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!R) return;
    var v = window.carrierVoice || (window.carrierVoice = { heard: [], listening: false, rec: null, wanted: false, mic: "", stream: null });
    v.wanted = true;
    try { if (v.rec) v.rec.abort(); } catch (e) { }
    var make = function () {
      var r = new R();
      r.lang = "en-US";
      r.continuous = true;
      r.interimResults = false;
      r.maxAlternatives = 5;
      r.onstart = function () { v.listening = true; };
      r.onerror = function (e) { v.heard.push("error|0|" + (e.error || "unknown")); };
      r.onresult = function (e) {
        for (var i = e.resultIndex; i < e.results.length; i++) {
          var res = e.results[i];
          if (!res.isFinal) continue;
          var guesses = [];
          for (var j = 0; j < res.length; j++) guesses.push(String(res[j].transcript || "").replace(/[\t|]/g, " ").trim());
          v.heard.push("ok|" + (res[0].confidence || 0).toFixed(2) + "|" + guesses.join("\t"));
        }
      };
      r.onend = function () {
        v.listening = false;
        if (v.rec === r) v.rec = null;
        // (Chrome ends a session by itself after a silence or a minute: again, while still held)
        if (v.wanted) { try { var again = make(); v.rec = again; go(again); return; } catch (e) { } }
        v.heard.push("end|0|");
      };
      return r;
    };
    // (with the chosen microphone's track where there is one; the default microphone otherwise)
    var go = function (r) {
      var track = v.stream && v.stream.active ? v.stream.getAudioTracks()[0] : null;
      try { if (track) r.start(track); else r.start(); } catch (e) { v.heard.push("error|0|" + e.message); }
    };
    var rec = make();
    v.rec = rec;
    if (v.mic && navigator.mediaDevices && !(v.stream && v.stream.active)) {
      navigator.mediaDevices.getUserMedia({ audio: { deviceId: { exact: v.mic } } }).then(function (s) {
        v.stream = s;
        if (v.wanted && v.rec === rec) go(rec);
      }).catch(function (e) {
        v.heard.push("error|0|the chosen microphone isn't there (" + (e.name || e.message) + "): the default one instead");
        if (v.wanted && v.rec === rec) go(rec);
      });
    } else go(rec);
  },

  // push-to-talk let go: what was said so far is finished and comes in
  CarrierVoiceStop: function () {
    var v = window.carrierVoice;
    if (v) v.wanted = false;
    if (v && v.rec) try { v.rec.stop(); } catch (e) { }
  },

  // the next thing heard: "ok|confidence|guess TAB guess ...", "error|0|why" or "end|0|" (0: nothing)
  CarrierVoicePoll: function () {
    var v = window.carrierVoice;
    if (!v || v.heard.length === 0) return 0;
    var s = v.heard.shift();
    var n = lengthBytesUTF8(s) + 1;
    var p = _malloc(n);
    stringToUTF8(s, p, n);
    return p;
  },

  // 1: recognition can be given a chosen microphone (SpeechRecognition.start(track): Chrome and
  // Edge 135 and later, and the browsers made from them); 0: only the default one. Other browsers
  // would take the track and quietly use the default, so it's told by the version.
  CarrierVoiceMicChoice: function () {
    try {
      if (!(window.SpeechRecognition || window.webkitSpeechRecognition)) return 0;
      if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices || !navigator.mediaDevices.getUserMedia) return 0;
      var m = /(?:Chrome|Chromium|Edg)\/(\d+)/.exec(navigator.userAgent);
      return m && parseInt(m[1], 10) >= 135 ? 1 : 0;
    } catch (e) { return 0; }
  },

  // ask the browser for its microphones (they come in by CarrierVoiceMics; their names only once
  // the page has been allowed the microphone)
  CarrierVoiceListMics: function () {
    var v = window.carrierVoice || (window.carrierVoice = { heard: [], listening: false, rec: null, wanted: false, mic: "", stream: null });
    if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) return;
    navigator.mediaDevices.enumerateDevices().then(function (ds) {
      var lines = [];
      for (var i = 0; i < ds.length; i++)
        if (ds[i].kind === "audioinput") lines.push(ds[i].deviceId + "\t" + String(ds[i].label || "").replace(/[\t\n]/g, " "));
      v.mics = lines.join("\n");
    }).catch(function () { v.mics = ""; });
  },

  // the microphones last listed: a line each, "device id TAB name" (0: not listed yet)
  CarrierVoiceMics: function () {
    var v = window.carrierVoice;
    if (!v || typeof v.mics !== "string") return 0;
    var n = lengthBytesUTF8(v.mics) + 1;
    var p = _malloc(n);
    stringToUTF8(v.mics, p, n);
    return p;
  },

  // the microphone to listen with ("": the default); the last one's stream is let go
  CarrierVoiceSetMic: function (idPtr) {
    var v = window.carrierVoice || (window.carrierVoice = { heard: [], listening: false, rec: null, wanted: false, mic: "", stream: null });
    var id = UTF8ToString(idPtr);
    if (id === v.mic) return;
    v.mic = id;
    if (v.stream) { try { v.stream.getTracks().forEach(function (t) { t.stop(); }); } catch (e) { } v.stream = null; }
  },
});
