import '/_content/FluentCMS.Web.UI.Components/js/quill.min.js'
import '/_content/FluentCMS.Web.UI.Components/js/quill-image-resize.min.js'
import { debounce } from '/_content/FluentCMS.Web.UI.Components/js/helpers.js'

const toolbarTypes = {
    basic: undefined,
    simple: [
        [{ header: [1, 2, 3, 4, 5, 6, false] }],
        ["bold", "italic", "underline", "strike"],
        ["blockquote", "code-block"],
    ],
    standard: [
        [{ header: [1, 2, 3, 4, 5, 6, false] }],
        ["bold", "italic", "underline", "strike"],
        ["blockquote", "code-block"],
        [{ list: "ordered" }, { list: "bullet" }],
        [{ direction: "rtl" }],
        [{ size: ["small", false, "large", "huge"] }],
        [{ color: [] }, { background: [] }],
        [{ font: [] }],
        [{ align: [] }],
        ["clean"],
    ],
    advanced: [
        [{ header: [1, 2, 3, 4, 5, 6, false] }],
        ["bold", "italic", "underline", "strike"],
        ["blockquote", "code-block"],
        [{ list: "ordered" }, { list: "bullet" }],
        [{ script: "sub" }, { script: "super" }],
        [{ indent: "-1" }, { indent: "+1" }],
        [{ direction: "rtl" }],
        [{ color: [] }, { background: [] }],
        [{ font: [] }],
        ["pageLink", "fileLink", "externalLink"],
        ["clean"],
    ],
};

const textEditors = new Map();

// https://github.com/slab/quill/issues/662#issuecomment-1623172474
function destroyQuill(instance, el) {
    if (!instance) return;

    instance.theme.modules.toolbar.container.remove();
    instance.theme.modules.clipboard.container.remove();
    instance.theme.tooltip.root.remove();

    el.classList.forEach((cls) => {
        if (cls.startsWith("ql-")) {
            requestAnimationFrame(() => {
                el.classList.remove(cls);
            });
        }
    });

    el.innerHTML = instance.root.innerHTML
}

export function update(dotnet, element, config) {
    const instance = textEditors.get(element)

    if ('value' in config) {
        instance.root.innerHTML = config.value
    }
}

export function setLink(dotnet, element, value) {
    const textEditor = textEditors.get(element)
    
    const href = value.href
    const text = value.text
    const mode = value.mode

    const range = textEditor.getSelection(true);

    textEditor.deleteText(range.index, range.length)
    if(mode == "Page") {
        textEditor.format('pageLink', {text, href})
    } else if(mode == "File") {
        textEditor.format('fileLink', {text, href})
    } else {
        textEditor.format('externalLink', {text, href})
    }
}

class BaseLinkModule extends Quill.imports["blots/inline"] {
    static blotName = 'pageLink';
    static tagName = 'a'

    static create(value) {
        const node = super.create();
        node.setAttribute('href', value.href);
        
        node.innerHTML = value.text;
        return node;
    }

    static value(node) {
        return {
          id: node.getAttribute('href'),
          text: node.innerHTML
        };
    }

    static formats(node) {
        return {            
            href: node.getAttribute('href'),
            text: node.innerHTML,
            id: node.getAttribute('href')
        }
    }
}

class PageLinkModule extends BaseLinkModule {
    static blotName = "pageLink";
}

class FileLinkModule extends BaseLinkModule {
    static blotName = "fileLink";
}

class ExternalLinkModule extends BaseLinkModule {
    static blotName = "externalLink";
}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    Quill.register('modules/resize', window.QuillResizeImage)

    Quill.register('formats/pageLink', PageLinkModule)
    Quill.register('formats/fileLink', FileLinkModule)
    Quill.register('formats/externalLink', ExternalLinkModule)

    const options = {
        modules: {
            toolbar: toolbarTypes[config.type],
            resize: {
                locale: {}
            }
        },
        theme: "snow",
        placeholder: config.placeholder,
        readOnly: config.readonly,
    }

    let instance = new Quill(element, options);
    
    const toolbar = instance.getModule('toolbar');

    function getLinkHandler(mode) {
        
        return () => {
            const range = instance.getSelection(true)
            const text = instance.getText(range.index, range.length)
            
            dotnet.invokeMethodAsync('OpenLinkModal', text, mode)
        }
    }

    toolbar.addHandler('pageLink', getLinkHandler('Page'))
    toolbar.addHandler('fileLink', getLinkHandler('File'))
    toolbar.addHandler('externalLink', getLinkHandler('External'))

    var pageLink = toolbar.container.querySelector('.ql-pageLink')
    var fileLink = toolbar.container.querySelector('.ql-fileLink')
    var externalLink = toolbar.container.querySelector('.ql-externalLink')

    pageLink.innerHTML = `
        <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24"><path fill="currentColor" d="M18.5 20a.5.5 0 0 1-.5.5h-4.229a5 5 0 0 1-.77 1.5H18a2 2 0 0 0 2-2V9.828a2 2 0 0 0-.586-1.414l-5.829-5.828l-.049-.04l-.036-.03a2 2 0 0 0-.219-.18a1 1 0 0 0-.08-.044l-.048-.024l-.05-.029c-.054-.031-.109-.063-.166-.087a2 2 0 0 0-.624-.138q-.03-.002-.059-.007L12.172 2H6a2 2 0 0 0-2 2v10h1.5V4a.5.5 0 0 1 .5-.5h6V8a2 2 0 0 0 2 2h4.5zm-5-15.379L17.378 8.5H14a.5.5 0 0 1-.5-.5zM5.75 15.75A.75.75 0 0 0 5 15l-.2.005A4 4 0 0 0 5 23l.102-.007A.75.75 0 0 0 5 21.5l-.164-.005A2.5 2.5 0 0 1 5 16.5l.102-.007a.75.75 0 0 0 .648-.743M13 19a4 4 0 0 0-4-4l-.102.007A.75.75 0 0 0 9 16.5l.164.005A2.5 2.5 0 0 1 9 21.5l-.102.007A.75.75 0 0 0 9 23l.2-.005A4 4 0 0 0 13 19m-4.25-.75h-3.5l-.102.007a.75.75 0 0 0 .102 1.493h3.5l.102-.007a.75.75 0 0 0-.102-1.493"/></svg>
    `

    fileLink.innerHTML = `
        <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24"><path fill="currentColor" d="M4 4.25A2.25 2.25 0 0 1 6.25 2h11.5A2.25 2.25 0 0 1 20 4.25v9.583a4.7 4.7 0 0 0-1.5-.326V4.25a.75.75 0 0 0-.75-.75H6.25a.75.75 0 0 0-.75.75v15.5c0 .414.336.75.75.75h4.316a4.8 4.8 0 0 0 1.268 1.5H6.25A2.25 2.25 0 0 1 4 19.75zM7.75 6.5a.75.75 0 0 0 0 1.5h8.5a.75.75 0 0 0 0-1.5zM7 11.75a.75.75 0 0 1 .75-.75h8.5a.75.75 0 0 1 0 1.5h-8.5a.75.75 0 0 1-.75-.75m15 6.5a3.75 3.75 0 0 0-3.75-3.75l-.102.007A.75.75 0 0 0 18.25 16l.154.005a2.25 2.25 0 0 1-.154 4.495l-.003.005l-.102.007a.75.75 0 0 0 .108 1.493V22l.2-.005A3.75 3.75 0 0 0 22 18.25m-6.5-3a.75.75 0 0 0-.75-.75l-.2.005a3.75 3.75 0 0 0 .2 7.495l.102-.007a.75.75 0 0 0-.102-1.493l-.154-.005A2.25 2.25 0 0 1 14.75 16l.102-.007a.75.75 0 0 0 .648-.743m3.5 3a.75.75 0 0 0-.75-.75h-3.5l-.102.007A.75.75 0 0 0 14.75 19h3.5l.102-.007A.75.75 0 0 0 19 18.25"/></svg>
    `

    externalLink.innerHTML = `
        <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 5H8.2c-1.12 0-1.68 0-2.108.218a1.999 1.999 0 0 0-.874.874C5 6.52 5 7.08 5 8.2v7.6c0 1.12 0 1.68.218 2.108a2 2 0 0 0 .874.874c.427.218.987.218 2.105.218h7.606c1.118 0 1.677 0 2.104-.218c.377-.192.683-.498.875-.874c.218-.428.218-.987.218-2.105V14m1-5V4m0 0h-5m5 0l-7 7"/></svg>
    `

    // TODO: Don't remove these Icons. will be used for image upload/external feature
    // toolbar.container.querySelector('.ql-imageExternal').innerHTML = `
    //     <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 5H8.2c-1.12 0-1.68 0-2.108.218a1.999 1.999 0 0 0-.874.874C5 6.52 5 7.08 5 8.2v7.6c0 1.12 0 1.68.218 2.108a2 2 0 0 0 .874.874c.427.218.987.218 2.105.218h7.606c1.118 0 1.677 0 2.104-.218c.377-.192.683-.498.875-.874c.218-.428.218-.987.218-2.105V14m1-5V4m0 0h-5m5 0l-7 7"/></svg>
    // `
    
    // toolbar.container.querySelector('.ql-imageUpload').innerHTML = `
    //     <svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 24 24"><path fill="currentColor" d="M19 13a1 1 0 0 0-1 1v.38l-1.48-1.48a2.79 2.79 0 0 0-3.93 0l-.7.7l-2.48-2.48a2.85 2.85 0 0 0-3.93 0L4 12.6V7a1 1 0 0 1 1-1h7a1 1 0 0 0 0-2H5a3 3 0 0 0-3 3v12a3 3 0 0 0 3 3h12a3 3 0 0 0 3-3v-5a1 1 0 0 0-1-1M5 20a1 1 0 0 1-1-1v-3.57l2.9-2.9a.79.79 0 0 1 1.09 0l3.17 3.17l4.3 4.3Zm13-1a.89.89 0 0 1-.18.53L13.31 15l.7-.7a.77.77 0 0 1 1.1 0L18 17.21Zm4.71-14.71l-3-3a1 1 0 0 0-.33-.21a1 1 0 0 0-.76 0a1 1 0 0 0-.33.21l-3 3a1 1 0 0 0 1.42 1.42L18 4.41V10a1 1 0 0 0 2 0V4.41l1.29 1.3a1 1 0 0 0 1.42 0a1 1 0 0 0 0-1.42"/></svg>
    // `

    if ('value' in config) {
        instance.root.innerHTML = config.value
    }

    const updateValue = debounce((delta, old, source) => {
        const innerHtml = instance.root.innerHTML;
        let value;
        if (innerHtml === "<p><br></p>")
            value = ""
        else
            value = innerHtml

        dotnet.invokeMethodAsync('UpdateValue', value)
    }, 50)

    instance.on('text-change', updateValue)

    textEditors.set(element, instance);
}

export function dispose(dotnet, element) {
    const textEditor = textEditors.get(element);

    destroyQuill(textEditor, element);

    textEditors.delete(element);
}
