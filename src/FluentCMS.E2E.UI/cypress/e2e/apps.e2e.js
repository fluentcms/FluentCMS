/// <reference types="cypress" />

describe('Apps CRUD', () => {
    before(() => {
        cy.doSetup()
    })

    it('Cancel create app', () => {
        cy.checkAppCreateCancel()
    })

    it('Create app', () => {
        cy.checkAppCreate()
    })

    it('Show app detail', () => {
        cy.checkAppDetail()
    })

    it('Cancel update app', () => {
        cy.checkAppUpdateCancel()
    })

    it('Update app', () => {
        cy.checkAppUpdate()
    })

    it('Cancel delete app', () => {
        cy.checkAppDeleteCancel()
    })

    it('Delete app', () => {
        cy.checkAppDelete()
    })
})
