/// <reference types="cypress" />

describe('Apps CRUD', () => {
  before(() => {
    cy.doSetup()
    cy.cleanApp()
  })

  it('Should not create app', () => {
    cy.navigateToAppCreatePage();
    cy.checkAppCreateCancel();
  })
  
  it('Should create app', () => {
    cy.navigateToAppCreatePage();
    cy.checkAppCreate();
  })

  it('Should show app', () => {
    cy.navigateToAppListPage();
    cy.checkAppDetail()
  })

  it('Should not update app', () => {
    cy.navigateToAppListPage()
    cy.checkAppUpdateCancel();
  })

  it('Should update app', () => {
    cy.navigateToAppListPage()
    cy.checkAppUpdate();
  })

  it('Should show apps', () => {
    cy.navigateToAppListPage()
    cy.checkAppList();
  })
  
  it('Should not delete app', () => {
    cy.navigateToAppListPage()
    cy.checkAppDeleteCancel();
  })

  it('Should delete app', () => {
    cy.navigateToAppListPage()
    cy.checkAppDelete();
  })
})