// Infinite scroll for the log list: an IntersectionObserver watches a sentinel element at the
// end of the list and asks the Blazor component for the next batch when it comes into view.
window.marvinLog = {
  observe(sentinel, root, dotNetRef) {
    const io = new IntersectionObserver(entries => {
      if (entries.some(e => e.isIntersecting)) {
        dotNetRef.invokeMethodAsync('LoadMoreAsync');
      }
    }, { root, rootMargin: '0px 0px 400px 0px' });
    io.observe(sentinel);
    sentinel._marvinObserver = io;
  },

  // An observer only reports changes, so if the sentinel is still visible after a batch was
  // added (tall screen, short rows) nothing fires. Re-observing reports the current state again.
  recheck(sentinel) {
    const io = sentinel?._marvinObserver;
    if (io) {
      io.unobserve(sentinel);
      io.observe(sentinel);
    }
  },

  dispose(sentinel) {
    sentinel?._marvinObserver?.disconnect();
  },

  scrollToTop(el, smooth) {
    el?.scrollTo({ top: 0, behavior: smooth ? 'smooth' : 'auto' });
  }
};

// Close the mobile account menu when tapping outside it
document.addEventListener('click', e => {
  document.querySelectorAll('details.account-menu[open]').forEach(d => {
    if (!d.contains(e.target)) d.open = false;
  });
});
