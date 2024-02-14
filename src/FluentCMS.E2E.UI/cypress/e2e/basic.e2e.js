/// <reference types="cypress" />

describe('basic', () => {
    before(() => {
        cy.visit('/')
    })

    it('setup page', () => {
        cy.checkSetup()
    })

    it('login page', () => {
        cy.visit('/auth/login')
        cy.shortWait()
        cy.checkLogin()
    })

    it('register page', () => {
        cy.visit('/auth/register')
        cy.shortWait()
        cy.checkRegister()
    })
})
