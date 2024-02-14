/// <reference types="cypress" />

import config from '../../config'

Cypress.Commands.add('checkSetup', (username, email, password) => {
    cy.get('#setupUsernameInput').clear()
    cy.get('#setupUsernameInput').type(username || config.setupUsername)

    cy.get('#setupEmailInput').clear()
    cy.get('#setupEmailInput').type(email || config.setupEmail)

    cy.get('#setupPasswordInput').clear()
    cy.get('#setupPasswordInput').type(password || config.setupPassword)

    cy.get('#setupSubmitButton').click()

    cy.shortWait()

    cy.dashboardShouldAvailable();
})
