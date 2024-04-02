import '../../../js/easymde.min.js'

const markdownEditors = new Map();

function debounce(cb, timeout = 300) {
    let timer;

    return (...args) => {
        if(timer) clearTimeout(timer)

        timer = setTimeout(() => {
            cb(args)
        }, timeout)
    }
}

export function close(dotnet, element) {
    const markdownEditor = markdownEditors.get(element);

    markdownEditor.hide();
}

export function open(dotnet, element) {
    const markdownEditor = markdownEditors.get(element);

    markdownEditor.show();
}

export function update(dotnet, element, config) {
    const instance = markdownEditors.get(element)

    if ('value' in config) {
        instance.value(config.value);
    }
}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    let instance = new EasyMDE({
        element,
        sideBySideFullscreen: false,
        direction: getComputedStyle(element).direction,
        sideBySideFullscreen: false,
        uploadImage: true,
        previewClass: 'editor-preview',
        toolbar: [
            "heading",
            "heading-1",
            "heading-2",
            "heading-3",
            "|",
            "bold",
            "italic",
            "strikethrough",
            "|",
            "code",
            "quote",
            "unordered-list",
            "ordered-list",
            "table",
            "clean-block",
            "link",
            // "image",
            // "upload-image",
            "horizontal-rule",
            "|",
            "side-by-side",
            "preview",
            "guide",
            
        ],
        async imageUploadFunction(file, onSuccess, onError) {
            // TODO: Handle file upload
            // onSuccess('file url')
        }
    })

    const updateValue = debounce(() => {
        dotnet.invokeMethodAsync('UpdateValue', instance.value())
    }, 50)

    instance.codemirror.on('change', updateValue)

    markdownEditors.set(element, instance);
}

export function dispose(dotnet, element) {
    const markdownEditor = markdownEditors.get(element);

    markdownEditor?.destroy();

    markdownEditors.delete(element);
}
