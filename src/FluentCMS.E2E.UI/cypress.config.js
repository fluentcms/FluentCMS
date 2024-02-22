const { defineConfig } = require("cypress");

module.exports = defineConfig({
    viewportWidth: 1366,
    viewportHeight: 800,
    e2e: {
        baseUrl: 'http://localhost:5000',
        specPattern: 'cypress/e2e/**/*.e2e.js',
        setupNodeEvents(on, config) {
            // implement node event listeners here
        },
    },
});
