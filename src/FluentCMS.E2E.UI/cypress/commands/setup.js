/// <reference types="cypress" />

import config from '../../config'

Cypress.Commands.add('checkSetup', (username, email, password) => {
    cy.doSetup(username, email, password)
})

Cypress.Commands.add('doSetup', (username, email, password) => {
    cy.visit('/').shortWait()
    
    cy.get('body').then($body => {
        if($body[0].querySelector('#setupUsernameInput')) {
            if(username) {
                cy.get('#setupUsernameInput').clear().type(username || config.setupUsername)
            }
            if(email) {
                cy.get('#setupEmailInput').clear().type(email || config.setupEmail)
            }
            if(password) {
                cy.get('#setupPasswordInput').clear().type(password || config.setupPassword)
            }

            cy.get('#setupSubmitButton').click()
            cy.shortWait()
            cy.dashboardShouldAvailable()
        }
    })
})