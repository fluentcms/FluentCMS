describe('Content Type CRUD', () => {
  const app = {
    title: 'First',
    slug: 'first',
    description: 'first description'
  }

  const contentType = {
    title: 'Posts',
    slug: 'posts',
    description: 'description of posts content type',
    fields: [
      {
        title: 'Title',
        slug: 'title',
        description: 'Description of title field',
        required: true,
        hint: 'title field',
        label: 'Title',
        placeholder: 'Enter Title...',
        defaultValue: ''
      },
      {
        title: 'Content',
        slug: 'content',
        description: 'Description of Content field',
        required: true,
        hint: 'Content field',
        label: 'Content',
        placeholder: 'Enter Content...',
        defaultValue: ''
      },
    ]
  }

  before(() => {
    cy.doSetup()
    cy.contentTypeClean()

    cy.appCreate(app)
    // cy.createSampleApps()
  })

  it('Should not create Content type', () => {
    cy.contentTypeCreateCancel()
  })

  it('Should create Content type', () => {
    cy.contentTypeCreate(app.title, contentType)
  })

  it('Should show Content type', () => {
    cy.contentTypeDetail(app.title, contentType)
  })

  it('Should not update Content type', () => {
    cy.contentTypeUpdateCancel(app.title, contentType.slug)
  })

  it('Should update Content type', () => {
    cy.contentTypeUpdate(app.title, contentType.slug, contentType)
  })

  it('Should show Content types', () => {
    cy.contentTypeList()
  })

  it('Should not delete Content type', () => {
    cy.contentTypeDeleteCancel(app.title, contentType.slug)
  })

  it('Should delete Content type', () => {
    cy.contentTypeDelete(app.title, contentType.slug)
  })
})