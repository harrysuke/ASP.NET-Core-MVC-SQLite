// Light/dark theme toggle.
// The initial data-bs-theme value is applied by an inline script in _Layout.cshtml
// (before CSS paints) to avoid a flash of the wrong theme. This file wires up the
// toggle buttons and keeps the user's choice in localStorage.
(function () {
    'use strict';

    var STORAGE_KEY = 'theme';
    var SELECTOR = '[data-theme-toggle]';
    var root = document.documentElement;

    function log() {
        console.log('[theme]', ...arguments);
    }

    function currentTheme() {
        return root.getAttribute('data-bs-theme') === 'dark' ? 'dark' : 'light';
    }

    function syncToggleState(btn, theme) {
        try {
            var isDark = theme === 'dark';
            var label = isDark ? 'Switch to light mode' : 'Switch to dark mode';
            btn.setAttribute('aria-label', label);
            btn.setAttribute('aria-pressed', String(isDark));
            btn.setAttribute('title', label);
            log('syncToggleState:', btn, 'theme=', theme, 'isDark=', isDark);
        } catch (e) {
            log('syncToggleState error:', e);
        }
    }

    function apply(theme) {
        log('apply() called with theme:', theme);
        var oldTheme = currentTheme();
        log('apply() oldTheme:', oldTheme);

        try {
            root.setAttribute('data-bs-theme', theme);
            log('apply() set data-bs-theme=', theme);
        } catch (e) {
            log('apply() setAttribute error:', e);
        }

        try {
            localStorage.setItem(STORAGE_KEY, theme);
            log('apply() localStorage.setItem success');
        } catch (e) {
            log('apply() localStorage error (storage unavailable):', e);
        }

        var themeColor = document.querySelector('meta[name="theme-color"]');
        if (themeColor) {
            var content = themeColor.getAttribute('data-theme-color-' + theme) || themeColor.content;
            themeColor.setAttribute('content', content);
            log('apply() theme-color meta updated to:', content);
        }

        var toggles = document.querySelectorAll(SELECTOR);
        log('apply() found', toggles.length, 'toggle(s) to sync');
        for (var i = 0; i < toggles.length; i++) {
            syncToggleState(toggles[i], theme);
        }

        try {
            var event = new CustomEvent('themechange', { detail: { theme: theme } });
            root.dispatchEvent(event);
            log('apply() themechange event dispatched');
        } catch (e) {
            log('apply() dispatchEvent error:', e);
        }

        // Validate CSS variables actually changed
        var bodyBg = getComputedStyle(root).getPropertyValue('--bs-body-bg').trim();
        var bodyColor = getComputedStyle(root).getPropertyValue('--bs-body-color').trim();
        log('apply() CSS vars --bs-body-bg:', bodyBg, '--bs-body-color:', bodyColor);

        log('[theme] applied:', theme);
    }

    // Single delegated handler: works even if the toggle buttons are added or
    // re-rendered after this script runs.
    document.addEventListener('click', function (event) {
        log('click event:', event.target, 'type:', event.type);
        var target = event.target && event.target.closest ? event.target.closest(SELECTOR) : null;
        log('click closest(' + SELECTOR + '):', target);
        if (!target) {
            log('click: no matching toggle, ignoring');
            return;
        }
        event.preventDefault();
        var newTheme = currentTheme() === 'dark' ? 'light' : 'dark';
        log('click: toggling to', newTheme);
        apply(newTheme);
    });

    // Sync the accessibility state of any toggle on first load.
    function init() {
        var initial = currentTheme();
        var toggles = document.querySelectorAll(SELECTOR);
        log('init() found toggles:', toggles.length, 'initial theme:', initial);
        for (var i = 0; i < toggles.length; i++) {
            syncToggleState(toggles[i], initial);
        }
    }

    // Watch for theme changes from any source (including inline script in head)
    var lastTheme = currentTheme();
    new MutationObserver(function () {
        var theme = currentTheme();
        if (theme !== lastTheme) {
            log('MutationObserver: theme changed from', lastTheme, 'to', theme);
            lastTheme = theme;
            var toggles = document.querySelectorAll(SELECTOR);
            for (var i = 0; i < toggles.length; i++) {
                syncToggleState(toggles[i], theme);
            }
        }
    }).observe(root, { attributes: true, attributeFilter: ['data-bs-theme'] });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Test hotkey: Ctrl+Shift+T to programmatically toggle and dump state
    document.addEventListener('keydown', function (event) {
        if (event.ctrlKey && event.shiftKey && event.key === 'T') {
            event.preventDefault();
            var theme = currentTheme();
            var newTheme = theme === 'dark' ? 'light' : 'dark';
            log('=== MANUAL TOGGLE (Ctrl+Shift+T) ===');
            log('Current theme:', theme);
            log('Toggling to:', newTheme);
            log('data-bs-theme attr:', root.getAttribute('data-bs-theme'));
            log('localStorage theme:', localStorage.getItem(STORAGE_KEY));
            var computedBg = getComputedStyle(root).getPropertyValue('--bs-body-bg').trim();
            var computedColor = getComputedStyle(root).getPropertyValue('--bs-body-color').trim();
            log('Computed --bs-body-bg:', computedBg);
            log('Computed --bs-body-color:', computedColor);
            var toggles = document.querySelectorAll(SELECTOR);
            log('Toggle buttons found:', toggles.length);
            toggles.forEach(function(btn, idx) {
                log('  Toggle', idx, 'aria-pressed:', btn.getAttribute('aria-pressed'), 'aria-label:', btn.getAttribute('aria-label'));
            });
            apply(newTheme);
        }
    });

    log('site.js loaded, SELECTOR:', SELECTOR, 'STORAGE_KEY:', STORAGE_KEY);
})();