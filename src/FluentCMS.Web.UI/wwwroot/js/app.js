// import { Tabs } from "flowbite";

const tabsElements = document.querySelectorAll(".f-tabs");

tabsElements.forEach((tabsElement) => {
    // create an array of objects with the id, trigger element (eg. button), and the content element
    const tabItems = tabsElement.querySelectorAll('.f-tab-list .f-tab-item');

    console.log(tabsElement)
    console.log(tabItems)
    const tabElements = [...tabItems].map(x => {
        return {
            id: x.getAttribute('id'),
            triggerEl: x,
            targetEl: document.querySelector(x.getAttribute('data-tabs-target'))
        }
    })

    // options with default values
    const options = {
        defaultTabId: tabElements[0]?.id,
        activeClasses:
            "f-tab-item-active",
        inactiveClasses:
            "f-tab-item-inactive",
        onShow: () => {
            console.log("tab is shown");
        },
    };

    // instance options with default values
    const instanceOptions = {
        // id: tabsElement ,
        id: "1",
        override: true,
    };

    console.log(tabsElement, tabElements, options, instanceOptions);
    const tabs = new Tabs(tabsElement, tabElements, options, instanceOptions);
});


const tabsElement = document.getElementById('tab-example');

// create an array of objects with the id, trigger element (eg. button), and the content element
const tabElements = [
    {
        id: 'profile',
        triggerEl: document.querySelector('#profile-tab-example'),
        targetEl: document.querySelector('#profile-example'),
    },
    {
        id: 'dashboard',
        triggerEl: document.querySelector('#dashboard-tab-example'),
        targetEl: document.querySelector('#dashboard-example'),
    },
    {
        id: 'settings',
        triggerEl: document.querySelector('#settings-tab-example'),
        targetEl: document.querySelector('#settings-example'),
    },
    {
        id: 'contacts',
        triggerEl: document.querySelector('#contacts-tab-example'),
        targetEl: document.querySelector('#contacts-example'),
    },
];

// options with default values
const options = {
    defaultTabId: 'settings',
    activeClasses:
        'text-blue-600 hover:text-blue-600 dark:text-blue-500 dark:hover:text-blue-400 border-blue-600 dark:border-blue-500',
    inactiveClasses:
        'text-gray-500 hover:text-gray-600 dark:text-gray-400 border-gray-100 hover:border-gray-300 dark:border-gray-700 dark:hover:text-gray-300',
    onShow: () => {
        console.log('tab is shown');
    },
};

// instance options with default values
const instanceOptions = {
  id: 'tabs-example',
  override: true
};
console.log({tabsElement, tabElements, options, instanceOptions})
const tabs = new Tabs(tabsElement, tabElements, options, instanceOptions);
