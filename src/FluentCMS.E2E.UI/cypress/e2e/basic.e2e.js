/// <reference types="cypress" />

describe('basic', () => {
    before(() => {
        cy.visit('/')
    })
    it('setup page', () => {
        cy.checkSetup()
    })
})
