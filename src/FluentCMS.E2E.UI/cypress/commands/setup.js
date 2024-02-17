/// <reference types="cypress" />

import config from '../../config'

Cypress.Commands.add('checkSetup', (username, email, password) => {
    cy.get('body').then($body => {
        if($body[0].querySelector('#setupUsernameInput')) {
            cy.get('#setupUsernameInput').clear()
            cy.get('#setupUsernameInput').type(username || config.setupUsername)

            cy.get('#setupEmailInput').clear()
            cy.get('#setupEmailInput').type(email || config.setupEmail)

            cy.get('#setupPasswordInput').clear()
            cy.get('#setupPasswordInput').type(password || config.setupPassword)

            cy.get('#setupSubmitButton').click()

            cy.shortWait()

            cy.dashboardShouldAvailable();
        }
    })
})

Cypress.Commands.add('doSetup', (username, email, password) => {
    cy.visit('/').shortWait()
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
            cy.shortWait()
            cy.dashboardShouldAvailable()
        }

    })

})