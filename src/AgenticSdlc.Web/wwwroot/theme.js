// Light/dark theme: persisted in localStorage, applied to <html data-theme>.
window.agenticTheme = {
    get: function () { return localStorage.getItem('theme') || 'dark'; },
    apply: function (t) {
        document.documentElement.dataset.theme = t;
        localStorage.setItem('theme', t);
        return t;
    },
    toggle: function () {
        var next = window.agenticTheme.get() === 'light' ? 'dark' : 'light';
        return window.agenticTheme.apply(next);
    },
};
