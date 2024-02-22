describe('Content Type CRUD', () => {
  before(() => {
    cy.doSetup()
    cy.cleanContentType()
    cy.createSampleApps()
  })

  it('Should not create Content type', () => {
    cy.checkContentTypeCreateCancel()
  })

  it('Should create Content type', () => {
    cy.checkContentTypeCreate()
  })

  it('Should show Content type', () => {
    cy.navigateToContentTypeListPage('First');
    cy.checkContentTypeDetail()
  })

  it('Should not update Content type', () => {
    cy.checkContentTypeUpdateCancel()
  })

  it('Should update Content type', () => {
    cy.checkContentTypeUpdate()
  })

  it('Should show Content types', () => {
    cy.checkContentTypeList()
  })

  it('Should not delete Content type', () => {
    cy.checkContentTypeDeleteCancel()
  })

  it('Should delete Content type', () => {
    cy.checkContentTypeDelete()
  })
})