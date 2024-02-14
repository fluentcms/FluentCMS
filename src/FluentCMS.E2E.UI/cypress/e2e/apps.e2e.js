describe('Apps CRUD', () => {
  it('Should clean apps', () => {
    // TODO: cy.doSetup() [submit setup form if didn't submitted before]
    cy.cleanApp()
  })

  it('Should not create app', () => {
    cy.checkAppCreateCancel();
  })
  
  it('Should create app', () => {
    cy.checkAppCreate();
  })

  it('Should show apps', () => {
    cy.checkAppList();
  })

  it('Should not update app', () => {
    cy.checkAppUpdateCancel();
  })

  it('Should update app', () => {
    cy.checkAppUpdate();
  })

  it('Should not delete app', () => {
    cy.checkAppDeleteCancel();
  })

  it('Should delete app', () => {
    cy.checkAppDelete();
  })
})