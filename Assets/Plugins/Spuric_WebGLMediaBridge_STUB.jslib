// Spuric_WebGLMediaBridge_STUB.jslib
// Put in: Assets/Plugins/WebGL/Spuric_WebGLMediaBridge_STUB.jslib
//
// Fixes WebGL IL2CPP link errors by defining these symbols:
//   Spuric_OpenFilePicker
//   Spuric_ShowVideoOverlay
//   Spuric_ShowVideoInRect
//   Spuric_HideInlineVideo
//   Spuric_HideVideoOverlay
//   Spuric_DownloadUrlAsFile
//
// SAFE NO-OP/STUB implementations so your WebGL build can succeed.

mergeInto(LibraryManager.library, {
  Spuric_OpenFilePicker: function() {
    console.warn('[Spuric] Spuric_OpenFilePicker STUB called. (No-op)');
  },
  Spuric_ShowVideoOverlay: function() {
    console.warn('[Spuric] Spuric_ShowVideoOverlay STUB called. (No-op)');
  },
  Spuric_ShowVideoInRect: function() {
    console.warn('[Spuric] Spuric_ShowVideoInRect STUB called. (No-op)');
  },
  Spuric_HideInlineVideo: function() {
    console.warn('[Spuric] Spuric_HideInlineVideo STUB called. (No-op)');
  },
  Spuric_HideVideoOverlay: function() {
    console.warn('[Spuric] Spuric_HideVideoOverlay STUB called. (No-op)');
  },
  Spuric_DownloadUrlAsFile: function() {
    console.warn('[Spuric] Spuric_DownloadUrlAsFile STUB called. (No-op)');
  }
});
