describe('Apps CRUD', () => {
  it('Should clean apps', () => {
    cy.navigateToAppListPage()

    // TODO: cy.doSetup() [submit setup form if didn't submitted before]
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