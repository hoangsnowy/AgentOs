// AgentOS theming: persisted in localStorage, applied to <html data-theme / data-wallpaper>.
// Three controls:
//   theme      : 'light' | 'dark'
//   wallpaper  : 'enterprise-light' | 'enterprise-dark' | 'aurora' | 'midnight' | 'sunset'
//   glass      : 0..100 (percent) → --glass-blur in pixels (0..32px)
window.agenticTheme = {
    THEME_KEY: 'theme',
    WALL_KEY: 'wallpaper',
    GLASS_KEY: 'glass',

    getTheme: function () { return localStorage.getItem(this.THEME_KEY) || 'light'; },
    getWallpaper: function () { return localStorage.getItem(this.WALL_KEY) || 'enterprise-light'; },
    getGlass: function () { var g = parseInt(localStorage.getItem(this.GLASS_KEY), 10); return isNaN(g) ? 0 : g; },

    applyTheme: function (t) {
        document.documentElement.dataset.theme = t;
        localStorage.setItem(this.THEME_KEY, t);
        return t;
    },
    applyWallpaper: function (w) {
        document.documentElement.dataset.wallpaper = w;
        localStorage.setItem(this.WALL_KEY, w);
        return w;
    },
    applyGlass: function (percent) {
        var p = Math.max(0, Math.min(100, parseInt(percent, 10) || 0));
        var blurPx = Math.round((p / 100) * 32);
        document.documentElement.style.setProperty('--glass-blur', blurPx + 'px');
        document.documentElement.style.setProperty('--glass-saturate', (100 + p) + '%');
        localStorage.setItem(this.GLASS_KEY, p);
        return p;
    },
    toggleTheme: function () {
        var next = this.getTheme() === 'light' ? 'dark' : 'light';
        return this.applyTheme(next);
    },

    // Back-compat with old call sites that used agenticTheme.apply / agenticTheme.toggle / agenticTheme.get
    get: function () { return this.getTheme(); },
    apply: function (t) { return this.applyTheme(t); },
    toggle: function () { return this.toggleTheme(); },

    restoreAll: function () {
        this.applyTheme(this.getTheme());
        this.applyWallpaper(this.getWallpaper());
        this.applyGlass(this.getGlass());
    },
};

// Apply persisted theming before Blazor mounts to avoid flash.
window.agenticTheme.restoreAll();

// Auth — Phase 8.3. Single place to clear every persisted auth key on logout/lock so a stale
// JWT can't linger after sign-out.
window.agenticAuth = {
    AUTH_KEYS: ['agentic-jwt', 'agentic-user', 'agentic-jwt-exp', 'agentic-signed-in'],
    signOut: function () {
        this.AUTH_KEYS.forEach(function (k) { localStorage.removeItem(k); });
    },
};
