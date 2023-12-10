(function () {
    window.QuillFunctions = {
        createQuill: function (dotnetHelper, quillElement, quillOptions = {}) {
            var options = {
                debug: "info",
                modules: {
                    toolbar: [
                        [{ header: [1, 2, 3, 4, 5, 6, false] }],
                        [{ font: [] }],

                        ["bold", "italic", "underline", "strike"],
                        [{ align: [] }],

                        ["blockquote", "code-block"],
                        [{ list: "ordered" }, { list: "bullet" }],
                        [{ script: "sub" }, { script: "super" }],
                        [{ indent: "-1" }, { indent: "+1" }],
                        [{ direction: "rtl" }],
                        [{ color: [] }, { background: [] }],
                        ["clean"],
                    ],
                },
                placeholder: "Compose an epic...",
                readOnly: false,
                theme: "snow",
                ...quillOptions,
            };
            // set quill at the object we can call
            // methods on later
            const instance = new Quill(quillElement, options);

            instance.on("text-change", function (delta, oldDelta, source) {
                // Get the HTML content of the editor
                var htmlContent = instance.root.innerHTML;

                // Call the .NET method using JavaScript Interop
                dotnetHelper.invokeMethodAsync("OnTextChanged", htmlContent);
            });
        },
        getQuillContent: function (quillControl) {
            return JSON.stringify(quillControl.__quill.getContents());
        },
    };
})();
