/// <reference types="cypress">

describe("Home", () => {

    beforeEach(() => {
        cy.doSetup();
    })

    before(() => {
        cy.doSetup();
        cy.navigateToHomePage();
    })

    it('Tour section should exist', () => {
        cy.homeTourShouldExist();
    })
    
    it('Version should exist', () => {
        cy.homeVersionShouldExist();
    })
    
    it('Tour section should work', () => {
        cy.homeTourButtonsShouldHaveCorrectLinks();
    })

    it('Home Links should work', () => {
        cy.navigateToHomePage();
        cy.homeLinksShouldWork();
    })
})