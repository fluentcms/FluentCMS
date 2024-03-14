/// <reference types="cypress" />

import config from '../../config'

Cypress.Commands.add('checkSetup', (username, email, password) => {
    cy.doSetup(username, email, password)
})

Cypress.Commands.add('doSetup', (username, email, password) => {
    email ??= config.setupEmail
    username ??= config.setupUsername
    password ??= config.setupPassword

    cy.visit('/').waitForNavigate()
    
    cy.get('body').then($body => {
        if($body[0].querySelector('#setupUsernameInput')) {
            if(username) {
                cy.get('#setupUsernameInput').clear().type(username)
            }
            if(email) {
                cy.get('#setupEmailInput').clear().type(email)
            }
            if(password) {
                cy.get('#setupPasswordInput').clear().type(password)
            }

            cy.get('#setupSubmitButton').click()
            cy.waitForNavigate()
            cy.dashboardShouldAvailable()
        } 

        cy.visit('/auth/login').waitForNavigate()
        cy.checkLogin(username, password)        
    })
})