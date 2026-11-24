// The wingman's live voice on the web: the browser's own speech (speechSynthesis), which adds
// nothing to the download. It plays by itself, outside the game's audio. Nothing here calls into
// the game: it asks (CarrierSpeaking), so nothing is left pointing into it when the page leaves.
mergeInto(LibraryManager.library, {
  CarrierSpeechSupported: function () {
    return window.speechSynthesis && window.SpeechSynthesisUtterance ? 1 : 0;
  },

  // speak a line (after any being spoken); a deeper English voice where the browser has one
  CarrierSpeak: function (text, volume) {
    var s = window.speechSynthesis;
    if (!s || !window.SpeechSynthesisUtterance) return 0;
    var u = new SpeechSynthesisUtterance(UTF8ToString(text));
    var voices = s.getVoices() || [];
    var en = voices.filter(function (v) { return /^en/i.test(v.lang); });
    var male = en.filter(function (v) { return /male|david|daniel|george|guy|mark|james|ryan/i.test(v.name) && !/female/i.test(v.name); });
    if (male.length) u.voice = male[0]; else if (en.length) u.voice = en[0];
    u.lang = u.voice ? u.voice.lang : "en-US";
    u.rate = 1.05;
    u.pitch = 0.85;
    u.volume = Math.max(0, Math.min(1, volume));
    s.speak(u);
    console.log("[Voice] speak (" + (u.voice ? u.voice.name : "the browser's voice") + ")");
    return 1;
  },

  CarrierSpeaking: function () {
    var s = window.speechSynthesis;
    return s && (s.speaking || s.pending) ? 1 : 0;
  },

  CarrierSpeakStop: function () {
    if (window.speechSynthesis) window.speechSynthesis.cancel();
  },
});
