describe('Apps CRUD', () => {
  it('Should clean apps', () => {
    // TODO: cy.doSetup() [submit setup form if didn't submitted before]
    cy.cleanApp()
  })
  
  it('Should create app', () => {
    cy.checkAppCreate();
  })

  it('Should show apps', () => {
    cy.checkAppList();
  })

  it('Should not delete app', () => {
    cy.checkAppDeleteCancel();
  })

  it('Should delete app', () => {
    cy.checkAppDelete();
  })

  it('Cancel button should work', () => {
    cy.checkAppCreateCancel();
  })

})