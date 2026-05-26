// Forward wheel events from ApexCharts canvases to the page so the user
// can scroll normally without leaving the chart area.
(function () {
    function applyScrollFix(canvas) {
        canvas.addEventListener('wheel', function (e) {
            e.stopPropagation();
            window.scrollBy({ top: e.deltaY, behavior: 'auto' });
        }, { passive: true });
    }

    function fixAll() {
        document.querySelectorAll('.apexcharts-canvas').forEach(function (el) {
            if (!el.dataset.scrollFixed) {
                el.dataset.scrollFixed = '1';
                applyScrollFix(el);
            }
        });
    }

    // Run once on load and re-run whenever new charts appear (Blazor re-renders)
    document.addEventListener('DOMContentLoaded', fixAll);
    const observer = new MutationObserver(fixAll);
    observer.observe(document.body, { childList: true, subtree: true });
})();
