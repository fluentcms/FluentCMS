/// <reference types="cypress" />

describe('Basic', () => {
    before(() => {
        cy.visit('/')
    })

    it('Setup page', () => {
        cy.checkSetup()
    })

    it('Login page', () => {
        cy.visit('/auth/login')
        cy.shortWait()
        cy.checkLogin()
    })

    it('Register page', () => {
        // cy.visit('/auth/register')
        // cy.shortWait()
        // cy.checkRegister()
    })
})
