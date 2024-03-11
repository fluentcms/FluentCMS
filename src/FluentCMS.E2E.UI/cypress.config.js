const { defineConfig } = require("cypress");

module.exports = defineConfig({
    viewportWidth: 1366,
    viewportHeight: 800,
    reporter: 'cypress-mochawesome-reporter',
    reporterOptions: {
      embeddedScreenshots: true,
      
    },
    retries: 1,
    video: true,
    e2e: {
        baseUrl: 'http://localhost:5000',
        specPattern: 'cypress/e2e/**/*.e2e.js',
        setupNodeEvents(on, config) {
            require('cypress-mochawesome-reporter/plugin')(on);
            // implement node event listeners here
        },
    },
});
