(function () {
    window.QuillFunctions = {
        createQuill: function (quillElement, quillOptions = {}) {
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
            new Quill(quillElement, options);
        },
        getQuillContent: function (quillControl) {
            return JSON.stringify(quillControl.__quill.getContents());
        },
    };
})();
