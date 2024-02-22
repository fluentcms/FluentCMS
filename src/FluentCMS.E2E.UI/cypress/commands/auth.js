/// <reference types="cypress" />

import config from '../../config'

Cypress.Commands.add('checkLogin', (username, password) => {
    cy.get('#loginUsernameInput').clear()
    cy.get('#loginUsernameInput').type(username || config.setupUsername)

    cy.get('#loginPasswordInput').clear()
    cy.get('#loginPasswordInput').type(password || config.setupPassword)

    cy.get('#loginSubmitButton').click()

    cy.shortWait()

    cy.dashboardShouldAvailable();
})


Cypress.Commands.add('checkRegister', (username, email, password) => {
    cy.get('#registerUsernameInput').clear()
    cy.get('#registerUsernameInput').type(username || config.registerUsername)

    cy.get('#registerEmailInput').clear()
    cy.get('#registerEmailInput').type(email || config.registerEmail)

    cy.get('#registerPasswordInput').clear()
    cy.get('#registerPasswordInput').type(password || config.registerPassword)

    cy.get('#registerSubmitButton').click()

    cy.shortWait()

    cy.dashboardShouldAvailable();
})
