/// <reference types="cypress" />

describe('Apps CRUD', () => {
    const app = {
        title: 'First App',
        slug: 'first-app',
        description: 'First App Description'
    }

    before(() => {
        cy.doSetup()
        cy.appClean()
    })

    it('Cancel create app', () => {
        cy.appCreateCancel()
    })

    it('Create app', () => {
        cy.appCreate(app)
    })

    it('Show app detail', () => {
        cy.appDetail(app)
    })

    it('Cancel update app', () => {
        cy.appUpdateCancel(app.slug)
    })

    it('Update app', () => {
        cy.appUpdate(app.slug, {
            title: 'New title',
            slug: 'new-slug',
            description: 'new-description'
        })
    })

    it('Cancel delete app', () => {
        cy.appDeleteCancel(app.slug)
    })

    it('Delete app', () => {
        cy.appDelete(app.slug)
    })
})
