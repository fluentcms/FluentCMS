describe('Content CRUD', () => {
  before(() => {
    cy.doSetup()
    cy.cleanContent()
  })

  // it.skip('Should not create Content', () => {
  //   cy.checkContentCreateCancel()
  // })

  it('Should create Content', () => {
    cy.checkContentCreate()
  })

  // it.skip('Should not update Content', () => {
  //   cy.checkContentUpdateCancel()
  // })

  it('Should update Content', () => {
    cy.checkContentUpdate()
  })

  it('Should show Contents', () => {
    cy.checkContentList()
  })

  // it.skip('Should not delete Content', () => {
  //   cy.checkContentDeleteCancel()
  // })

  it('Should delete Content', () => {
    cy.checkContentDelete()
  })
})