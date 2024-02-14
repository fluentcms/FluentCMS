/// <reference types="cypress" />

describe('Admin Panel', () => {
    before(() => {
        cy.visit('/')
    })

    it('Check Navbar', () => {
        cy.checkAdminNavbar()
    })
    
    it('The sidebar should exist.', () => {
        cy.visit('/')
        cy.adminSidebarShouldAvailable()
        cy.adminSidebarLogoShouldAvailable()
    })

    

    it('The theme button should change theme when clicked.', () => {
        cy.visit('/')
        cy.checkAdminSidebarThemeToggle()
    })

    it('Should navigate to home page if clicked logo or Home link', () => {
        cy.visit('/')
        cy.checkAdminHomeLinks()
    })

    it('Clicking on links should redirect to correct pages', () => {
        cy.visit('/')
        cy.checkAdminSidebarNavigations()
    })
})
