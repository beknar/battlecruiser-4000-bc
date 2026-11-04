// Calls from the game to the web page around it (Assets/WebGLTemplates/StarSystem).
mergeInto(LibraryManager.library, {
  // shows which graphics API the game started with (WebGPU, or the WebGL 2 fallback)
  CarrierPageGraphics: function (name) {
    var el = document.getElementById("api");
    var text = UTF8ToString(name);
    if (el) el.textContent = text;
    console.log("[Page] graphics: " + text + (el ? "" : " (no badge on this page)"));
  },
});
