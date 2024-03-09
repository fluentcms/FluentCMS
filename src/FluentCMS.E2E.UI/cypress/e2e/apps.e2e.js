/// <reference types="cypress" />

import config from "../../config"

describe('Apps CRUD', () => {
    const app = config.apps[0]

    before(() => {
        cy.doSetup()

        cy.navigateToAppListPage()
        cy.appClean()
    })

    it('Cancel create app', () => {
        cy.navigateToAppCreatePage()

        cy.appCreateCancel()
    })

    it('Create app', () => {
        cy.navigateToAppCreatePage()

        cy.appCreate(app)
    })

    it('Show app detail', () => {
        cy.navigateToAppListPage()

        cy.appDetail(app)
    })

    it('Cancel update app', () => {
        cy.navigateToAppListPage()

        cy.appUpdateCancel(app.slug)
    })

    it('Update app', () => {
        cy.navigateToAppListPage()

        cy.appUpdate(app.slug, config.apps[1])
    })

    it('Cancel delete app', () => {
        cy.navigateToAppListPage()
        cy.appDeleteCancel(app.slug)
    })

    it('Delete app', () => {
        cy.navigateToAppListPage()
        cy.appDelete(app.slug)
    })
})
