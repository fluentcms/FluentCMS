/// <reference types="cypress" />

describe('Admin Panel', () => {
    beforeEach(() => {
        cy.visit('/')
    })

    it('The sidebar should exist.', () => {
        cy.adminSidebarShouldAvailable()
        cy.adminSidebarLogoShouldAvailable()
    })

    it('Check Navbar', () => {
        cy.checkAdminNavbar()
    })

    it('The theme button should change theme when clicked.', () => {
        cy.checkAdminSidebarThemeToggle()
    })

    it('Should navigate to home page if clicked logo or Home link', () => {
        cy.checkAdminHomeLinks()
    })

    it('Clicking on links should redirect to correct pages', () => {
        cy.checkAdminSidebarNavigations()
    })
})
