let installed = false;

function handleNavigationClick(event) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
        return;
    }

    const target = event.target;
    if (!(target instanceof Element)) {
        return;
    }

    const anchor = target.closest("aside a[href], .sp-workspace a[href]");
    if (!anchor || anchor.hasAttribute("download") || (anchor.target && anchor.target !== "_self")) {
        return;
    }

    const destination = new URL(anchor.href, document.baseURI);
    if (destination.origin !== window.location.origin) {
        return;
    }

    event.preventDefault();
    event.stopImmediatePropagation();
    window.location.assign(destination.href);
}

export function install() {
    if (installed) {
        return;
    }

    document.addEventListener("click", handleNavigationClick, true);
    installed = true;
}

export function uninstall() {
    if (!installed) {
        return;
    }

    document.removeEventListener("click", handleNavigationClick, true);
    installed = false;
}
