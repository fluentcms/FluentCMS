/// <reference types="cypress" />

Cypress.Commands.add('checkSetup', () => {
    cy.get('#setupSubmitButton').click()

    cy.shortWait()

    cy.get('main')
        .invoke('text')
        .then(text => text.trim())
        .should('be.empty')
})
