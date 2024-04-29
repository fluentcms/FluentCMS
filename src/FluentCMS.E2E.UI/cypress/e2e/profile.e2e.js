import config from "../../config"

describe('Profile', () => {
    const testPassword = '1qaz!QAZ'
  
  beforeEach(() => {
    cy.doSetup()
  })

  it("Detail page", () => {
    cy.navigateToProfileDetailPage();

    // check something...
  })
  
  it("Should not change profile", () => {
    cy.navigateToProfileUpdatePage();

    cy.profileUpdateCancel({email: config.setupEmail}, {email: 'updated@gmail.com'})
    // check something...
  })

  it("Should change profile info", () => {
    cy.navigateToProfileUpdatePage();

    cy.profileUpdate({email: config.setupEmail}, {email: 'updated@gmail.com'})
  })

  it("Should change password and undo change password", () => {
    cy.navigateToProfileUpdatePage();

    cy.profileChangePassword(config.setupPassword, testPassword)

    cy.visit('/auth/login').shortWait()
    cy.checkLogin(config.setupUsername, testPassword)
  
    cy.navigateToProfileUpdatePage();

    cy.profileChangePassword(testPassword, config.setupPassword)
  })
})