/// <reference types="cypress" />

import config from '../../config'

Cypress.Commands.add('checkLogin', (username, password) => {
    cy.get('#loginUsernameInput').clear()
    cy.get('#loginUsernameInput').type(username || config.setupUsername, {delay: 10})

    cy.get('#loginPasswordInput').clear()
    cy.get('#loginPasswordInput').type(password || config.setupPassword, {delay: 10})

    cy.get('#loginSubmitButton').click()

    cy.shortWait()

    cy.dashboardShouldAvailable();
})


Cypress.Commands.add('checkRegister', (username, email, password) => {
    cy.get('#registerUsernameInput').clear()
    cy.get('#registerUsernameInput').type(username || config.registerUsername, {delay: 10})

    cy.get('#registerEmailInput').clear()
    cy.get('#registerEmailInput').type(email || config.registerEmail, {delay: 10})

    cy.get('#registerPasswordInput').clear()
    cy.get('#registerPasswordInput').type(password || config.registerPassword, {delay: 10})

    cy.get('#registerSubmitButton').click()

    cy.shortWait()

    cy.dashboardShouldAvailable();
})
