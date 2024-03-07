describe('Content CRUD', () => {
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

  const content = {
    title: 'Title of post',
    content: 'content of post'
  }

  before(() => {
    cy.doSetup()
    cy.cleanContent()

    cy.appCreate(app)
    cy.contentTypeCreate(app.title, contentType)
  })

  it('Should not create Content', () => {
    cy.contentCreateCancel(app.title, contentType.title)
  })

  it('Should create Content', () => {
    cy.contentCreate(app.title, contentType.title, content)
  })

  it('Should not update Content', () => {
    cy.contentUpdateCancel(app.title, contentType.title, content.title)
  })

  it('Should update Content', () => {
    cy.contentUpdate(app.title, contentType.title, content.title, {
      title: 'updated title',
      content: 'updated content'
    })
  })

  it('Should show Contents', () => {
    cy.contentList(app.title, contentType.title)
  })

  it('Should not delete Content', () => {
    cy.contentDeleteCancel(app.title, contentType.title, 'updated title')
  })

  it('Should delete Content', () => {
    cy.contentDelete(app.title, contentType.title, 'updated title')
  })
})