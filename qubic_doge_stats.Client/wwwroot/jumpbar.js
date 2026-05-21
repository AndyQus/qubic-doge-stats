window.jumpBarInit = function () {
    const bar = document.getElementById('jump-bar');
    if (!bar) return;
    const scroller = document.querySelector('.mud-main-content') || window;
    const onScroll = () => {
        const scrollTop = scroller === window ? window.scrollY : scroller.scrollTop;
        if (scrollTop > 120) bar.classList.remove('hidden');
        else bar.classList.add('hidden');
    };
    scroller.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
};

window.scrollToAnchor = function (id) {
    const el = document.getElementById(id);
    if (!el) return;
    const scroller = document.querySelector('.mud-main-content') || window;
    const top = el.getBoundingClientRect().top + (scroller === window ? window.scrollY : scroller.scrollTop) - 8;
    scroller.scrollTo({ top, behavior: 'smooth' });
};
