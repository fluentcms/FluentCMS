// ***********************************************
// This example commands.js shows you how to
// create various custom commands and overwrite
// existing commands.
//
// For more comprehensive examples of custom
// commands please read more here:
// https://on.cypress.io/custom-commands
// ***********************************************
//
//
// -- This is a parent command --
// Cypress.Commands.add('login', (email, password) => { ... })
//
//
// -- This is a child command --
// Cypress.Commands.add('drag', { prevSubject: 'element'}, (subject, options) => { ... })
//
//
// -- This is a dual command --
// Cypress.Commands.add('dismiss', { prevSubject: 'optional'}, (subject, options) => { ... })
//
//
// -- This will overwrite an existing command --
// Cypress.Commands.overwrite('visit', (originalFn, url, options) => { ... })

Cypress.Commands.add('shortWait', () => cy.wait(2500))
Cypress.Commands.add('mediumWait', () => cy.wait(5000))
Cypress.Commands.add('longWait', () => cy.wait(7500))

Cypress.Commands.add('elementShouldAvailable', (selector) => {
    cy.get(selector).should('exist')
    cy.get(selector).should('be.visible')
    cy.get(selector).should('have.length', 1)
})

Cypress.Commands.add('elementShouldUnavailable', (selector) => {
    cy.get(selector).should('not.exist')
})
