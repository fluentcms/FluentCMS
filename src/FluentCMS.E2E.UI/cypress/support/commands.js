/// <reference types="cypress" />

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

Cypress.Commands.add('waitForNavigate', () => cy.wait(250))
Cypress.Commands.add('shortWait', () => cy.wait(750))
Cypress.Commands.add('mediumWait', () => cy.wait(1000))
Cypress.Commands.add('longWait', () => cy.wait(1500))

Cypress.Commands.add('elementShouldAvailable', (selector) => {
    cy.get(selector).should('exist')
    cy.get(selector).should('be.visible')
    cy.get(selector).should('have.length', 1)
})

Cypress.Commands.add('elementShouldUnavailable', (selector) => {
    cy.get(selector).should('not.exist')
})

Cypress.Commands.add('dashboardShouldAvailable', () => {
    cy.get('main').contains('Welcome').should('be.visible')
})

Cypress.Commands.add('deleteRow', {prevSubject: 'element'}, ($el, confirm = true, force = false) => {
    cy.shortWait()
    cy.get('.f-confirm').should('not.exist')
  
    cy.wrap($el).find('[data-test="delete-btn"]').click({force})
    cy.get('.f-confirm-content').should('be.visible')
    if(confirm) {
        cy.get('.f-confirm-content').contains('Yes, I\'m sure').click()
    } else {
        cy.get('.f-confirm-content').contains('No, cancel').click()
    }

    cy.shortWait()
    // TODO: Should enable this
    // cy.get('.f-confirm').should('not.exist')

})

Cypress.Commands.add('deleteTableRows', {prevSubject: 'element'}, ($table) => {
    function removeRows() {
        return cy.wrap($table).then(async $rows => {
            if($rows[0].querySelector('.f-table-row')) {
                await cy.wrap($rows).rows().first().deleteRow()

                removeRows()
            }
        })
    }
    removeRows()
})

Cypress.Commands.add('rows', { prevSubject: 'element' }, ($table, filter) => {
    let $rows = $table.find('.f-table-row');

    if($rows.length && filter) {
        return cy.wrap($rows).filter(`:contains("${filter}")`)
    }

    if($rows.length) {
        return $rows
    } else {
        return []
    }
});

Cypress.Commands.add('shot', (name) => {
    cy.screenshot(name)
})