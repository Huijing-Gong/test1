mergeInto(LibraryManager.library, {
  SideA_AcquireMouseControls: function () {
    var canvas = Module.canvas;
    if (!canvas) return;
    var state = Module.sideAMouseControls;
    if (!state) {
      state = {
        canvas: canvas,
        owners: 0,
        preventContextMenu: function (event) { event.preventDefault(); }
      };
      Module.sideAMouseControls = state;
      canvas.addEventListener('contextmenu', state.preventContextMenu, false);
    }
    // Retry can briefly overlap old/new scene ownership. Removing the old
    // scene must not remove the new scene's active canvas listener.
    state.owners += 1;
  },

  SideA_ReleaseMouseControls: function () {
    var state = Module.sideAMouseControls;
    if (!state) return;
    state.owners -= 1;
    if (state.owners > 0) return;
    state.canvas.removeEventListener('contextmenu', state.preventContextMenu, false);
    delete Module.sideAMouseControls;
  }
});
