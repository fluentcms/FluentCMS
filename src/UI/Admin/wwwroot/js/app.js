const prefix = "f-";
const confirmEls = document.querySelectorAll("." + prefix + "confirm");
const confirms = new Map();

const modalEls = document.querySelectorAll("." + prefix + "modal");
const modals = new Map();

for (const confirmEl of confirmEls) {
    const wrapper = confirmEl.querySelector("." + prefix + "confirm-wrapper");
    const confirmBtn = confirmEl.querySelector("button.f-confirm-button-yes");
    const cancelBtn = confirmEl.querySelector("button.f-confirm-button-no");
    const closeBtn = confirmEl.querySelector("button.f-close-button");

    async function open() {
        if (confirmEl.classList.contains(prefix + "confirm-visible")) return;

        confirmEl.classList.add(prefix + "confirm-visible");

        let backdrop = document.querySelector(
            "." + prefix + "confirm-backdrop"
        );
        if (!backdrop) {
            backdrop = document.createElement("div");
            backdrop.classList.add(prefix + "confirm-backdrop");
            confirmEl.insertAdjacentElement("afterend", backdrop);
        }

        return new Promise((resolve) => {
            function close() {
                confirmEl.classList.remove(prefix + "confirm-visible");
                backdrop?.remove();
            }

            function cancel() {
                cleanup();
                resolve(false);
            }

            function confirm() {
                cleanup();
                resolve(true);
            }

            function stopPropagation(e) {
                e.stopPropagation();
            }

            function cleanup() {
                confirmEl.removeEventListener("click", cancel);
                closeBtn.removeEventListener("click", cancel);
                cancelBtn.removeEventListener("click", cancel);
                confirmBtn.removeEventListener("click", confirm);
                wrapper.removeEventListener("click", stopPropagation);
                close();
            }

            confirmEl.addEventListener("click", cancel, { once: true }); // backdrop click
            closeBtn.addEventListener("click", cancel, { once: true });
            cancelBtn.addEventListener("click", cancel, { once: true });
            confirmBtn.addEventListener("click", confirm, { once: true });
            wrapper.addEventListener("click", stopPropagation);
        });
    }

    confirms.set(confirmEl.dataset.name, { open, element: confirmEl });
}

for (const modalEl of modalEls) {
    const wrapper = modalEl.querySelector("." + prefix + "modal-wrapper");
    const modalBtn = modalEl.querySelector("button.f-modal-button-yes");
    const cancelBtn = modalEl.querySelector("button.f-modal-button-no");
    const closeBtn = modalEl.querySelector("button.f-close-button");

    function bind() {
        modalEl.addEventListener("click", close, { once: true }); // backdrop click
        closeBtn.addEventListener("click", close, { once: true });
        wrapper.addEventListener("click", stopPropagation);
    }

    async function open() {
        bind();
        if (modalEl.classList.contains(prefix + "modal-visible")) return;

        modalEl.classList.add(prefix + "modal-visible");

        let backdrop = document.querySelector("." + prefix + "modal-backdrop");
        if (!backdrop) {
            backdrop = document.createElement("div");
            backdrop.classList.add(prefix + "modal-backdrop");
            modalEl.insertAdjacentElement("afterend", backdrop);
        }
    }

    async function close() {
        unbind();
        if (!modalEl.classList.contains(prefix + "modal-visible")) return;
        modalEl.classList.remove(prefix + "modal-visible");

        let backdrop = document.querySelector("." + prefix + "modal-backdrop");
        if (backdrop) {
            backdrop.remove();
            // backdrop = document.createElement("div");
            // backdrop.classList.add(prefix + "modal-backdrop");
            // modalEl.insertAdjacentElement("afterend", backdrop);
        }
    }

    function stopPropagation(e) {
        e.stopPropagation();
    }

    function unbind() {
        modalEl.removeEventListener("click", close);
        closeBtn.removeEventListener("click", close);
        wrapper.removeEventListener("click", stopPropagation);
    }
    
    modals.set(modalEl.dataset.name, { open, close, element: modalEl });
}

