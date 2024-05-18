/******/ (() => { // webpackBootstrap
/******/ 	var __webpack_modules__ = ({

/***/ 607:
/***/ (() => {

    var themeToggleDarkIcon = document.getElementById('theme-toggle-dark-icon');
    var themeToggleLightIcon = document.getElementById('theme-toggle-light-icon');
    
    // Change the icons inside the button based on previous settings
    if (localStorage.getItem('color-theme') === 'dark' || (!('color-theme' in localStorage) && window.matchMedia('(prefers-color-scheme: dark)').matches)) {
        themeToggleLightIcon.classList.remove('hidden');
    } else {
        themeToggleDarkIcon.classList.remove('hidden');
    }
    
    var themeToggleBtn = document.getElementById('theme-toggle');
    
    let event = new Event('dark-mode');
    
    themeToggleBtn.addEventListener('click', function() {
    
        // toggle icons
        themeToggleDarkIcon.classList.toggle('hidden');
        themeToggleLightIcon.classList.toggle('hidden');
    
        // if set via local storage previously
        if (localStorage.getItem('color-theme')) {
            if (localStorage.getItem('color-theme') === 'light') {
                document.documentElement.classList.add('dark');
                localStorage.setItem('color-theme', 'dark');
            } else {
                document.documentElement.classList.remove('dark');
                localStorage.setItem('color-theme', 'light');
            }
    
        // if NOT set via local storage previously
        } else {
            if (document.documentElement.classList.contains('dark')) {
                document.documentElement.classList.remove('dark');
                localStorage.setItem('color-theme', 'light');
            } else {
                document.documentElement.classList.add('dark');
                localStorage.setItem('color-theme', 'dark');
            }
        }
    
        document.dispatchEvent(event);
        
    });
    
    
    /***/ }),
    
    /***/ 414:
    /***/ (() => {
    
    const isSidebarExpanded = toggleSidebarEl => {
        return toggleSidebarEl.getAttribute('aria-expanded') === 'true' ? true : false;
    }
    
    const toggleSidebar = (sidebarEl, expand, setExpanded = false) => {
        const bottomMenuEl = document.querySelector('[sidebar-bottom-menu]');
        const mainContentEl = document.getElementById('main-content');
        if (expand) {
            sidebarEl.classList.add('lg:w-64');
            sidebarEl.classList.remove('lg:w-16');
            mainContentEl.classList.add('lg:ml-64');
            mainContentEl.classList.remove('lg:ml-16');
    
            document.querySelectorAll('#' + sidebarEl.getAttribute('id') + ' [sidebar-toggle-item]').forEach(sidebarToggleEl => {
                sidebarToggleEl.classList.remove('lg:hidden');
                sidebarToggleEl.classList.remove('lg:absolute');
            });
    
            // toggle multi level menu item initial and full text
            document.querySelectorAll('#' + sidebar.getAttribute('id') + ' ul > li > ul > li > a').forEach(e => {
                e.classList.add('pl-11');
                e.classList.remove('px-4');
                e.childNodes[0].classList.remove('hidden');
                e.childNodes[1].classList.add('hidden');
            });
    
            bottomMenuEl.classList.remove('flex-col', 'space-y-4', 'p-2');
            bottomMenuEl.classList.add('space-x-4', 'p-4');
            setExpanded ? toggleSidebarEl.setAttribute('aria-expanded', 'true') : null;
        } else {
            sidebarEl.classList.remove('lg:w-64');
            sidebarEl.classList.add('lg:w-16');
            mainContentEl.classList.remove('lg:ml-64');
            mainContentEl.classList.add('lg:ml-16');
            document.querySelectorAll('#' + sidebarEl.getAttribute('id') + ' [sidebar-toggle-item]').forEach(sidebarToggleEl => {
                sidebarToggleEl.classList.add('lg:hidden');
                sidebarToggleEl.classList.add('lg:absolute');
            });
    
            // toggle multi level menu item initial and full text
            document.querySelectorAll('#' + sidebar.getAttribute('id') + ' ul > li > ul > li > a').forEach(e => {
                e.classList.remove('pl-11');
                e.classList.add('px-4');
                e.childNodes[0].classList.add('hidden');
                e.childNodes[1].classList.remove('hidden');
            });
    
            bottomMenuEl.classList.add('flex-col', 'space-y-4', 'p-2');
            bottomMenuEl.classList.remove('space-x-4', 'p-4');
            setExpanded ? toggleSidebarEl.setAttribute('aria-expanded', 'false') : null;
        }
    }
    
    const toggleSidebarEl = document.getElementById('toggleSidebar');
    const sidebar = document.getElementById('sidebar');
    
    document.querySelectorAll('#' + sidebar.getAttribute('id') + ' ul > li > ul > li > a').forEach(e => {
        var fullText = e.textContent;
        var firstLetter = fullText.substring(0, 1);
    
        var fullTextEl = document.createElement('span');
        var firstLetterEl = document.createElement('span');
        firstLetterEl.classList.add('hidden');
        fullTextEl.textContent = fullText;
        firstLetterEl.textContent = firstLetter;
    
        e.textContent = '';
        e.appendChild(fullTextEl);
        e.appendChild(firstLetterEl);
    });
    
    // initialize sidebar
    if (localStorage.getItem('sidebarExpanded') !== null) {
        if (localStorage.getItem('sidebarExpanded') === 'true') {
            toggleSidebar(sidebar, true, false);
        } else {
            toggleSidebar(sidebar, false, true);
        }
    }
    
    toggleSidebarEl.addEventListener('click', () => {
        localStorage.setItem('sidebarExpanded', !isSidebarExpanded(toggleSidebarEl));
        toggleSidebar(sidebar, !isSidebarExpanded(toggleSidebarEl), true);
    });
    
    sidebar.addEventListener('mouseenter', () => {
        if (!isSidebarExpanded(toggleSidebarEl)) {
            toggleSidebar(sidebar, true);
        }
    });
    
    sidebar.addEventListener('mouseleave', () => {
        if (!isSidebarExpanded(toggleSidebarEl)) {
            toggleSidebar(sidebar, false);
        }
    });
    
    const toggleSidebarMobile = (sidebar, sidebarBackdrop, toggleSidebarMobileHamburger, toggleSidebarMobileClose) => {
        sidebar.classList.toggle('hidden');
        sidebarBackdrop.classList.toggle('hidden');
        toggleSidebarMobileHamburger.classList.toggle('hidden');
        toggleSidebarMobileClose.classList.toggle('hidden');
    }
    
    const toggleSidebarMobileEl = document.getElementById('toggleSidebarMobile');
    const sidebarBackdrop = document.getElementById('sidebarBackdrop');
    const toggleSidebarMobileHamburger = document.getElementById('toggleSidebarMobileHamburger');
    const toggleSidebarMobileClose = document.getElementById('toggleSidebarMobileClose');
    const toggleSidebarMobileSearch = document.getElementById('toggleSidebarMobileSearch');
    
    toggleSidebarMobileSearch.addEventListener('click', () => {
        toggleSidebarMobile(sidebar, sidebarBackdrop, toggleSidebarMobileHamburger, toggleSidebarMobileClose);
    });
    
    toggleSidebarMobileEl.addEventListener('click', () => {
        toggleSidebarMobile(sidebar, sidebarBackdrop, toggleSidebarMobileHamburger, toggleSidebarMobileClose);
    });
    
    sidebarBackdrop.addEventListener('click', () => {
        toggleSidebarMobile(sidebar, sidebarBackdrop, toggleSidebarMobileHamburger, toggleSidebarMobileClose);
    });
    
    /***/ })
    
    /******/ 	});
    /************************************************************************/
    /******/ 	// The module cache
    /******/ 	var __webpack_module_cache__ = {};
    /******/ 	
    /******/ 	// The require function
    /******/ 	function __webpack_require__(moduleId) {
    /******/ 		// Check if module is in cache
    /******/ 		var cachedModule = __webpack_module_cache__[moduleId];
    /******/ 		if (cachedModule !== undefined) {
    /******/ 			return cachedModule.exports;
    /******/ 		}
    /******/ 		// Create a new module (and put it into the cache)
    /******/ 		var module = __webpack_module_cache__[moduleId] = {
    /******/ 			// no module.id needed
    /******/ 			// no module.loaded needed
    /******/ 			exports: {}
    /******/ 		};
    /******/ 	
    /******/ 		// Execute the module function
    /******/ 		__webpack_modules__[moduleId](module, module.exports, __webpack_require__);
    /******/ 	
    /******/ 		// Return the exports of the module
    /******/ 		return module.exports;
    /******/ 	}
    /******/ 	
    /************************************************************************/
    /******/ 	/* webpack/runtime/compat get default export */
    /******/ 	(() => {
    /******/ 		// getDefaultExport function for compatibility with non-harmony modules
    /******/ 		__webpack_require__.n = (module) => {
    /******/ 			var getter = module && module.__esModule ?
    /******/ 				() => (module['default']) :
    /******/ 				() => (module);
    /******/ 			__webpack_require__.d(getter, { a: getter });
    /******/ 			return getter;
    /******/ 		};
    /******/ 	})();
    /******/ 	
    /******/ 	/* webpack/runtime/define property getters */
    /******/ 	(() => {
    /******/ 		// define getter functions for harmony exports
    /******/ 		__webpack_require__.d = (exports, definition) => {
    /******/ 			for(var key in definition) {
    /******/ 				if(__webpack_require__.o(definition, key) && !__webpack_require__.o(exports, key)) {
    /******/ 					Object.defineProperty(exports, key, { enumerable: true, get: definition[key] });
    /******/ 				}
    /******/ 			}
    /******/ 		};
    /******/ 	})();
    /******/ 	
    /******/ 	/* webpack/runtime/hasOwnProperty shorthand */
    /******/ 	(() => {
    /******/ 		__webpack_require__.o = (obj, prop) => (Object.prototype.hasOwnProperty.call(obj, prop))
    /******/ 	})();
    /******/ 	
    /************************************************************************/
    var __webpack_exports__ = {};
    // This entry need to be wrapped in an IIFE because it need to be in strict mode.
    (() => {
    "use strict";
    /* harmony import */ var _sidebar__WEBPACK_IMPORTED_MODULE_0__ = __webpack_require__(414);
    /* harmony import */ var _sidebar__WEBPACK_IMPORTED_MODULE_0___default = /*#__PURE__*/__webpack_require__.n(_sidebar__WEBPACK_IMPORTED_MODULE_0__);
    /* harmony import */ var _dark_mode__WEBPACK_IMPORTED_MODULE_1__ = __webpack_require__(607);
    /* harmony import */ var _dark_mode__WEBPACK_IMPORTED_MODULE_1___default = /*#__PURE__*/__webpack_require__.n(_dark_mode__WEBPACK_IMPORTED_MODULE_1__);
    // import '../css/input.css';
    // import 'svgmap/dist/svgMap.min.css';
    
    // import 'flowbite';
    
    // import './charts';
    // import './map';
    // import './kanban';
    
    // import './calendar';
    
    // Have the courage to follow your heart and intuition.
    
    })();
    
    /******/ })()
    ;
    //# sourceMappingURL=app.bundle.js.map