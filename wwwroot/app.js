"use strict";

const State = {
    currentUser: null,
    users: [],
    projects: [],
    activeBug: null,
    comments: [],
    bugDetailReturnTo: "homeView",
    activeProjectIdForVersions: 0,
    versionsForActiveProject: [],
    apiKeys: [],
    metadata: {
        Statuses: {},
        Priorities: {},
        DefaultStatus: "",
        DefaultPriority: ""
    }
};

function escapeHtml(value) {
    if (value === null || value === undefined) {
        return "";
    }
    const text = String(value);
    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}

function formatTimestamp(isoString) {
    if (!isoString) {
        return "";
    }
    const dateValue = new Date(isoString);
    if (isNaN(dateValue.getTime())) {
        return isoString;
    }
    return dateValue.toLocaleString();
}

function statusLabel(statusValue) {
    const label = State.metadata.Statuses[statusValue];
    if (label) {
        return label;
    }
    return statusValue;
}

function priorityLabel(priorityValue) {
    const label = State.metadata.Priorities[priorityValue];
    if (label) {
        return label;
    }
    return priorityValue;
}

function compareOptionPairsByLabel(pairA, pairB) {
    if (pairA.label < pairB.label) {
        return -1;
    }
    if (pairA.label > pairB.label) {
        return 1;
    }
    return 0;
}

function populateOptionSelect(selectElement, labelDictionary, allLabel, selectedValue) {
    selectElement.innerHTML = "";
    if (allLabel !== null && !selectElement.multiple) {
        const allOption = document.createElement("option");
        allOption.value = "";
        allOption.textContent = allLabel;
        selectElement.appendChild(allOption);
    }
    const pairs = [];
    const keys = Object.keys(labelDictionary);
    const keyCount = keys.length;
    for (let keyIndex = 0; keyIndex < keyCount; keyIndex++) {
        const key = keys[keyIndex];
        pairs.push({ value: key, label: labelDictionary[key] });
    }
    pairs.sort(compareOptionPairsByLabel);
    const pairCount = pairs.length;
    for (let pairIndex = 0; pairIndex < pairCount; pairIndex++) {
        const pair = pairs[pairIndex];
        const optionElement = document.createElement("option");
        optionElement.value = pair.value;
        optionElement.textContent = pair.label;
        if (selectedValue !== null && selectedValue !== undefined && pair.value === selectedValue) {
            optionElement.selected = true;
        }
        selectElement.appendChild(optionElement);
    }
}

async function loadMetadata() {
    const metadata = await apiRequest("GET", "/api/metadata");
    State.metadata.Statuses = metadata.Statuses;
    State.metadata.Priorities = metadata.Priorities;
    State.metadata.DefaultStatus = metadata.DefaultStatus;
    State.metadata.DefaultPriority = metadata.DefaultPriority;

    const statusFilterIds = [
        "homeNewStatusFilter",
        "homeModifiedStatusFilter",
        "homeUnassignedStatusFilter",
        "userCreatedStatusFilter",
        "userAssignedStatusFilter",
        "browseStatusFilter"
    ];
    const priorityFilterIds = [
        "homeNewPriorityFilter",
        "homeModifiedPriorityFilter",
        "homeUnassignedPriorityFilter",
        "userCreatedPriorityFilter",
        "userAssignedPriorityFilter",
        "browsePriorityFilter"
    ];

    const statusPairs = dictionaryToPairs(State.metadata.Statuses);
    const statusFilterCount = statusFilterIds.length;
    for (let statusFilterIndex = 0; statusFilterIndex < statusFilterCount; statusFilterIndex++) {
        createDropdownFilter(statusFilterIds[statusFilterIndex], "Status", statusPairs);
    }
    const priorityPairs = dictionaryToPairs(State.metadata.Priorities);
    const priorityFilterCount = priorityFilterIds.length;
    for (let priorityFilterIndex = 0; priorityFilterIndex < priorityFilterCount; priorityFilterIndex++) {
        createDropdownFilter(priorityFilterIds[priorityFilterIndex], "Priority", priorityPairs);
    }

    populateOptionSelect(document.getElementById("bugStatus"), State.metadata.Statuses, null, State.metadata.DefaultStatus);
    populateOptionSelect(document.getElementById("bugPriority"), State.metadata.Priorities, null, State.metadata.DefaultPriority);
}

async function apiRequest(method, path, body) {
    const requestInit = {
        method: method,
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin"
    };
    if (body !== undefined && body !== null) {
        requestInit.body = JSON.stringify(body);
    }
    const response = await fetch(path, requestInit);
    let payload = null;
    const responseText = await response.text();
    if (responseText.length > 0) {
        try {
            payload = JSON.parse(responseText);
        } catch (parseError) {
            payload = { error: responseText };
        }
    }
    if (!response.ok) {
        let message = "Request failed.";
        if (payload && payload.error) {
            message = payload.error;
        }
        const errorObject = new Error(message);
        errorObject.status = response.status;
        throw errorObject;
    }
    return payload;
}

function showView(viewId) {
    const allViews = document.querySelectorAll(".view");
    let viewIndex = 0;
    while (viewIndex < allViews.length) {
        allViews[viewIndex].classList.add("hidden");
        viewIndex++;
    }
    const targetView = document.getElementById(viewId);
    if (targetView) {
        targetView.classList.remove("hidden");
    }
}

function showHeader(visible) {
    const headerElement = document.getElementById("appHeader");
    if (visible) {
        headerElement.classList.remove("hidden");
    } else {
        headerElement.classList.add("hidden");
    }
}

async function handleLoginSubmit(submitEvent) {
    submitEvent.preventDefault();
    const usernameInput = document.getElementById("loginUsername");
    const passwordInput = document.getElementById("loginPassword");
    const errorElement = document.getElementById("loginError");
    errorElement.textContent = "";
    try {
        const user = await apiRequest("POST", "/api/auth/login", {
            Username: usernameInput.value,
            Password: passwordInput.value
        });
        State.currentUser = user;
        await onLoggedIn();
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

async function handleLogoutClick() {
    try {
        await apiRequest("POST", "/api/auth/logout", {});
    } catch (apiError) {
        // ignore
    }
    State.currentUser = null;
    showHeader(false);
    showView("loginView");
    document.getElementById("loginPassword").value = "";
}

async function loadUsers() {
    State.users = await apiRequest("GET", "/api/users");
    populateAssigneeDropdowns();
}

async function loadProjects() {
    State.projects = await apiRequest("GET", "/api/projects");
}

function fillProjectSelect(selectId, includePlaceholder, selectedId) {
    const select = document.getElementById(selectId);
    select.innerHTML = "";

    if (State.projects.length === 0 && includePlaceholder) {
        const emptyPlaceholder = document.createElement("option");
        emptyPlaceholder.value = "";
        emptyPlaceholder.textContent = "(no projects defined)";
        select.appendChild(emptyPlaceholder);
        return;
    }

    if (includePlaceholder) {
        const placeholder = document.createElement("option");
        placeholder.value = "";
        placeholder.textContent = "(select a project)";
        select.appendChild(placeholder);
    }

    const projectCount = State.projects.length;
    for (let projectIndex = 0; projectIndex < projectCount; projectIndex++) {
        const project = State.projects[projectIndex];
        const optionElement = document.createElement("option");
        optionElement.value = String(project.Id);
        optionElement.textContent = project.Name;
        if (selectedId && String(project.Id) === String(selectedId)) {
            optionElement.selected = true;
        }
        select.appendChild(optionElement);
    }
}

async function fetchVersionsForProject(projectId) {
    if (!projectId || projectId === "0" || projectId === 0) {
        return [];
    }
    return await apiRequest("GET", "/api/projects/" + projectId + "/versions");
}

function fillVersionSelect(selectId, versions, selectedId) {
    const select = document.getElementById(selectId);
    select.innerHTML = "";

    const placeholder = document.createElement("option");
    placeholder.value = "";
    placeholder.textContent = "(none)";
    select.appendChild(placeholder);

    const versionCount = versions.length;
    for (let versionIndex = 0; versionIndex < versionCount; versionIndex++) {
        const version = versions[versionIndex];
        const optionElement = document.createElement("option");
        optionElement.value = String(version.Id);
        optionElement.textContent = version.Name;
        if (selectedId && String(version.Id) === String(selectedId)) {
            optionElement.selected = true;
        }
        select.appendChild(optionElement);
    }
}

async function handleBugProjectChange() {
    const projectId = document.getElementById("bugProject").value;
    const versions = await fetchVersionsForProject(projectId);
    fillVersionSelect("bugFoundInVersion", versions, null);
}

async function handleDetailBugProjectChange() {
    const projectId = document.getElementById("detailBugProject").value;
    const versions = await fetchVersionsForProject(projectId);
    fillVersionSelect("detailBugFoundInVersion", versions, null);
    fillVersionSelect("detailBugFixedInVersion", versions, null);
}

function populateAssigneeDropdowns() {
    createDropdownFilter("browseAssigneeFilter", "Assignee", usersToPairs());

    const bugAssignee = document.getElementById("bugAssignee");
    const detailAssignee = document.getElementById("detailBugAssignee");
    bugAssignee.innerHTML = '<option value="">Unassigned</option>';
    detailAssignee.innerHTML = '<option value="">Unassigned</option>';

    const userCount = State.users.length;
    for (let userIndex = 0; userIndex < userCount; userIndex++) {
        const user = State.users[userIndex];
        const userLabel = user.DisplayName + " (" + user.Username + ")";

        const editOption = document.createElement("option");
        editOption.value = String(user.Id);
        editOption.textContent = userLabel;
        bugAssignee.appendChild(editOption);

        const detailOption = document.createElement("option");
        detailOption.value = String(user.Id);
        detailOption.textContent = userLabel;
        detailAssignee.appendChild(detailOption);
    }
}

const DropdownFilters = {};

function createDropdownFilter(containerId, labelPrefix, pairs) {
    DropdownFilters[containerId] = {
        labelPrefix: labelPrefix,
        pairs: pairs.slice(),
        isOpen: false
    };
    renderDropdownFilter(containerId);
}

function renderDropdownFilter(containerId) {
    const container = document.getElementById(containerId);
    if (!container) {
        return;
    }
    const instance = DropdownFilters[containerId];

    container.innerHTML = "";
    container.classList.add("dropdown-filter");

    const button = document.createElement("button");
    button.type = "button";
    button.className = "dropdown-filter-button";
    button.dataset.filterId = containerId;
    button.addEventListener("click", handleDropdownButtonClick);
    container.appendChild(button);

    const panel = document.createElement("div");
    panel.className = "dropdown-filter-panel hidden";
    panel.dataset.filterId = containerId;

    const toggleLink = document.createElement("button");
    toggleLink.type = "button";
    toggleLink.className = "dropdown-filter-select-all";
    toggleLink.dataset.filterId = containerId;
    toggleLink.addEventListener("click", handleDropdownSelectAllClick);
    panel.appendChild(toggleLink);

    const divider = document.createElement("div");
    divider.className = "dropdown-filter-divider";
    panel.appendChild(divider);

    const sortedPairs = instance.pairs.slice();
    sortedPairs.sort(compareOptionPairsByLabel);
    const pairCount = sortedPairs.length;
    for (let pairIndex = 0; pairIndex < pairCount; pairIndex++) {
        const pair = sortedPairs[pairIndex];
        const optionLabel = document.createElement("label");
        optionLabel.className = "dropdown-filter-option";
        const optionCheckbox = document.createElement("input");
        optionCheckbox.type = "checkbox";
        optionCheckbox.value = pair.value;
        optionCheckbox.checked = true;
        optionCheckbox.dataset.filterId = containerId;
        optionCheckbox.dataset.role = "option";
        optionCheckbox.addEventListener("change", handleDropdownOptionChange);
        optionLabel.appendChild(optionCheckbox);
        optionLabel.appendChild(document.createTextNode(" " + pair.label));
        panel.appendChild(optionLabel);
    }

    container.appendChild(panel);

    refreshDropdownFilterButton(containerId);
    refreshDropdownFilterSelectAllText(containerId);
}

function refreshDropdownFilterButton(containerId) {
    const container = document.getElementById(containerId);
    if (!container) {
        return;
    }
    const instance = DropdownFilters[containerId];
    const button = container.querySelector(".dropdown-filter-button");
    if (!button) {
        return;
    }
    const checkedValues = readCheckboxGroup(containerId);
    const totalCount = instance.pairs.length;
    let labelText = instance.labelPrefix + ": none";
    if (checkedValues.length === totalCount && totalCount > 0) {
        labelText = instance.labelPrefix + ": all";
    } else if (checkedValues.length > 0) {
        labelText = instance.labelPrefix + ": " + checkedValues.length + " selected";
    }
    button.textContent = labelText;
}

function refreshDropdownFilterSelectAllText(containerId) {
    const container = document.getElementById(containerId);
    if (!container) {
        return;
    }
    const toggleLink = container.querySelector(".dropdown-filter-select-all");
    if (!toggleLink) {
        return;
    }
    const optionCheckboxes = container.querySelectorAll("input[data-role='option']");
    const optionCount = optionCheckboxes.length;
    let allChecked = optionCount > 0;
    for (let optionIndex = 0; optionIndex < optionCount; optionIndex++) {
        if (!optionCheckboxes[optionIndex].checked) {
            allChecked = false;
            break;
        }
    }
    if (allChecked) {
        toggleLink.textContent = "Clear all";
    } else {
        toggleLink.textContent = "Select all";
    }
}

function handleDropdownButtonClick(clickEvent) {
    const button = clickEvent.currentTarget;
    const filterId = button.dataset.filterId;
    const wasOpen = DropdownFilters[filterId].isOpen;
    closeAllDropdownFilters();
    if (!wasOpen) {
        openDropdownFilter(filterId);
    }
}

function openDropdownFilter(filterId) {
    const container = document.getElementById(filterId);
    if (!container) {
        return;
    }
    const panel = container.querySelector(".dropdown-filter-panel");
    panel.classList.remove("hidden");
    DropdownFilters[filterId].isOpen = true;
}

function closeAllDropdownFilters() {
    const filterIds = Object.keys(DropdownFilters);
    const filterCount = filterIds.length;
    for (let filterIndex = 0; filterIndex < filterCount; filterIndex++) {
        const filterId = filterIds[filterIndex];
        const instance = DropdownFilters[filterId];
        if (!instance.isOpen) {
            continue;
        }
        const container = document.getElementById(filterId);
        if (!container) {
            continue;
        }
        const panel = container.querySelector(".dropdown-filter-panel");
        if (panel) {
            panel.classList.add("hidden");
        }
        instance.isOpen = false;
    }
}

function handleDropdownSelectAllClick(clickEvent) {
    const toggleLink = clickEvent.currentTarget;
    const filterId = toggleLink.dataset.filterId;
    const container = document.getElementById(filterId);
    if (!container) {
        return;
    }
    const optionCheckboxes = container.querySelectorAll("input[data-role='option']");
    const optionCount = optionCheckboxes.length;
    let allChecked = optionCount > 0;
    for (let optionIndex = 0; optionIndex < optionCount; optionIndex++) {
        if (!optionCheckboxes[optionIndex].checked) {
            allChecked = false;
            break;
        }
    }
    const targetState = !allChecked;
    for (let optionIndex = 0; optionIndex < optionCount; optionIndex++) {
        optionCheckboxes[optionIndex].checked = targetState;
    }
    refreshDropdownFilterSelectAllText(filterId);
    refreshDropdownFilterButton(filterId);
    dispatchDropdownChangeEvent(filterId);
}

function handleDropdownOptionChange(changeEvent) {
    changeEvent.stopPropagation();
    const filterId = changeEvent.currentTarget.dataset.filterId;
    refreshDropdownFilterSelectAllText(filterId);
    refreshDropdownFilterButton(filterId);
    dispatchDropdownChangeEvent(filterId);
}

function dispatchDropdownChangeEvent(filterId) {
    const container = document.getElementById(filterId);
    if (!container) {
        return;
    }
    const syntheticEvent = new Event("change", { bubbles: true });
    container.dispatchEvent(syntheticEvent);
}

function handleDocumentClickForDropdowns(clickEvent) {
    const filterIds = Object.keys(DropdownFilters);
    const filterCount = filterIds.length;
    let clickedInside = false;
    for (let filterIndex = 0; filterIndex < filterCount; filterIndex++) {
        const filterId = filterIds[filterIndex];
        const container = document.getElementById(filterId);
        if (container && container.contains(clickEvent.target)) {
            clickedInside = true;
            break;
        }
    }
    if (!clickedInside) {
        closeAllDropdownFilters();
    }
}

function readCheckboxGroup(containerId) {
    const container = document.getElementById(containerId);
    if (!container) {
        return [];
    }
    const checkboxes = container.querySelectorAll("input[type='checkbox'][data-role='option']");
    const values = [];
    const checkboxCount = checkboxes.length;
    for (let checkboxIndex = 0; checkboxIndex < checkboxCount; checkboxIndex++) {
        if (checkboxes[checkboxIndex].checked) {
            values.push(checkboxes[checkboxIndex].value);
        }
    }
    return values;
}

function dictionaryToPairs(dict) {
    const pairs = [];
    const keys = Object.keys(dict);
    const keyCount = keys.length;
    for (let keyIndex = 0; keyIndex < keyCount; keyIndex++) {
        const key = keys[keyIndex];
        pairs.push({ value: key, label: dict[key] });
    }
    return pairs;
}

function usersToPairs() {
    const pairs = [];
    const userCount = State.users.length;
    for (let userIndex = 0; userIndex < userCount; userIndex++) {
        const user = State.users[userIndex];
        pairs.push({ value: String(user.Id), label: user.DisplayName + " (" + user.Username + ")" });
    }
    return pairs;
}

async function loadBugSection(config) {
    const queryParts = [];
    let abortEmpty = false;

    if (config.statusContainerId) {
        const statusValues = readCheckboxGroup(config.statusContainerId);
        if (statusValues.length === 0) {
            abortEmpty = true;
        } else {
            queryParts.push("status=" + encodeURIComponent(statusValues.join(",")));
        }
    }
    if (config.priorityContainerId) {
        const priorityValues = readCheckboxGroup(config.priorityContainerId);
        if (priorityValues.length === 0) {
            abortEmpty = true;
        } else {
            queryParts.push("priority=" + encodeURIComponent(priorityValues.join(",")));
        }
    }
    if (config.assigneeContainerId) {
        const assigneeValues = readCheckboxGroup(config.assigneeContainerId);
        if (assigneeValues.length === 0) {
            abortEmpty = true;
        } else {
            queryParts.push("assignedTo=" + encodeURIComponent(assigneeValues.join(",")));
        }
    }
    if (config.sortSelectId) {
        const sortElement = document.getElementById(config.sortSelectId);
        if (sortElement && sortElement.value) {
            queryParts.push("sort=" + encodeURIComponent(sortElement.value));
        }
    }
    if (abortEmpty) {
        renderBugRows(config.tableBodyId, config.emptyId, []);
        return;
    }
    if (config.extraParams) {
        const extraKeys = Object.keys(config.extraParams);
        const extraKeyCount = extraKeys.length;
        for (let extraKeyIndex = 0; extraKeyIndex < extraKeyCount; extraKeyIndex++) {
            const extraKey = extraKeys[extraKeyIndex];
            queryParts.push(extraKey + "=" + encodeURIComponent(config.extraParams[extraKey]));
        }
    }
    if (config.includeClosedCheckboxId) {
        const includeClosedElement = document.getElementById(config.includeClosedCheckboxId);
        if (includeClosedElement && !includeClosedElement.checked) {
            queryParts.push("excludeClosed=true");
        }
    }

    let queryString = "";
    if (queryParts.length > 0) {
        queryString = "?" + queryParts.join("&");
    }

    const bugs = await apiRequest("GET", "/api/bugs" + queryString);
    renderBugRows(config.tableBodyId, config.emptyId, bugs);
}

function renderBugRows(tableBodyId, emptyId, bugs) {
    const tableBody = document.getElementById(tableBodyId);
    const emptyElement = document.getElementById(emptyId);
    tableBody.innerHTML = "";

    if (bugs.length === 0) {
        emptyElement.classList.remove("hidden");
        return;
    }
    emptyElement.classList.add("hidden");

    const bugCount = bugs.length;
    for (let bugIndex = 0; bugIndex < bugCount; bugIndex++) {
        const bug = bugs[bugIndex];
        const row = document.createElement("tr");
        row.dataset.bugId = String(bug.Id);

        let assigneeText = "Unassigned";
        if (bug.AssignedToDisplayName) {
            assigneeText = bug.AssignedToDisplayName;
        }

        row.innerHTML =
            "<td>" + escapeHtml(bug.Id) + "</td>" +
            "<td>" + escapeHtml(bug.Title) + "</td>" +
            "<td><span class=\"badge badge-status-" + escapeHtml(bug.Status) + "\">" + escapeHtml(statusLabel(bug.Status)) + "</span></td>" +
            "<td><span class=\"badge badge-priority-" + escapeHtml(bug.Priority) + "\">" + escapeHtml(priorityLabel(bug.Priority)) + "</span></td>" +
            "<td>" + escapeHtml(assigneeText) + "</td>" +
            "<td>" + escapeHtml(formatTimestamp(bug.UpdatedAt)) + "</td>";

        row.addEventListener("click", handleBugRowClick);
        tableBody.appendChild(row);
    }
}

function buildLast24hIso() {
    const millisPerDay = 24 * 60 * 60 * 1000;
    return new Date(Date.now() - millisPerDay).toISOString();
}

async function refreshHomeNewSection() {
    await loadBugSection({
        tableBodyId: "homeNewTbody",
        emptyId: "homeNewEmpty",
        statusContainerId: "homeNewStatusFilter",
        priorityContainerId: "homeNewPriorityFilter",
        extraParams: { createdSince: buildLast24hIso(), excludeClosed: "true" }
    });
}

async function refreshHomeModifiedSection() {
    await loadBugSection({
        tableBodyId: "homeModifiedTbody",
        emptyId: "homeModifiedEmpty",
        statusContainerId: "homeModifiedStatusFilter",
        priorityContainerId: "homeModifiedPriorityFilter",
        extraParams: { updatedSince: buildLast24hIso(), excludeClosed: "true" }
    });
}

async function refreshHomeUnassignedSection() {
    await loadBugSection({
        tableBodyId: "homeUnassignedTbody",
        emptyId: "homeUnassignedEmpty",
        statusContainerId: "homeUnassignedStatusFilter",
        priorityContainerId: "homeUnassignedPriorityFilter",
        extraParams: { unassigned: "true", excludeClosed: "true" }
    });
}

async function refreshHomeView() {
    await refreshHomeNewSection();
    await refreshHomeModifiedSection();
    await refreshHomeUnassignedSection();
}

async function refreshUserCreatedSection() {
    await loadBugSection({
        tableBodyId: "userCreatedTbody",
        emptyId: "userCreatedEmpty",
        statusContainerId: "userCreatedStatusFilter",
        priorityContainerId: "userCreatedPriorityFilter",
        includeClosedCheckboxId: "userCreatedIncludeClosed",
        extraParams: { createdBy: String(State.currentUser.Id) }
    });
}

async function refreshUserAssignedSection() {
    await loadBugSection({
        tableBodyId: "userAssignedTbody",
        emptyId: "userAssignedEmpty",
        statusContainerId: "userAssignedStatusFilter",
        priorityContainerId: "userAssignedPriorityFilter",
        includeClosedCheckboxId: "userAssignedIncludeClosed",
        extraParams: { assignedTo: String(State.currentUser.Id) }
    });
}

async function refreshUserView() {
    await refreshUserCreatedSection();
    await refreshUserAssignedSection();
}

async function loadBrowseSection() {
    await loadBugSection({
        tableBodyId: "browseTbody",
        emptyId: "browseEmpty",
        statusContainerId: "browseStatusFilter",
        priorityContainerId: "browsePriorityFilter",
        assigneeContainerId: "browseAssigneeFilter",
        sortSelectId: "browseSortFilter"
    });
}

function handleBugRowClick(clickEvent) {
    const row = clickEvent.currentTarget;
    const bugId = Number(row.dataset.bugId);
    openBugDetail(bugId);
}

async function openBugDetail(bugId) {
    State.activeBug = await apiRequest("GET", "/api/bugs/" + bugId);
    State.comments = await apiRequest("GET", "/api/bugs/" + bugId + "/comments");
    await renderBugDetail();
    showView("bugDetailView");
}

async function renderBugDetail() {
    const bug = State.activeBug;
    document.getElementById("bugDetailIdHeading").textContent = "Bug #" + bug.Id;
    document.getElementById("detailBugTitle").value = bug.Title;
    document.getElementById("detailBugDescription").value = bug.Description;
    populateOptionSelect(document.getElementById("detailBugStatus"), State.metadata.Statuses, null, bug.Status);
    populateOptionSelect(document.getElementById("detailBugPriority"), State.metadata.Priorities, null, bug.Priority);

    await loadProjects();
    fillProjectSelect("detailBugProject", false, String(bug.ProjectId));
    populateAssigneeDropdowns();
    if (bug.AssignedTo) {
        document.getElementById("detailBugAssignee").value = String(bug.AssignedTo);
    } else {
        document.getElementById("detailBugAssignee").value = "";
    }

    const versions = await fetchVersionsForProject(String(bug.ProjectId));
    let foundInId = null;
    if (bug.FoundInVersionId) {
        foundInId = String(bug.FoundInVersionId);
    }
    let fixedInId = null;
    if (bug.FixedInVersionId) {
        fixedInId = String(bug.FixedInVersionId);
    }
    fillVersionSelect("detailBugFoundInVersion", versions, foundInId);
    fillVersionSelect("detailBugFixedInVersion", versions, fixedInId);

    document.getElementById("detailBugReporter").textContent = bug.CreatedByDisplayName;
    document.getElementById("detailBugCreated").textContent = formatTimestamp(bug.CreatedAt);
    document.getElementById("detailBugUpdated").textContent = formatTimestamp(bug.UpdatedAt);
    document.getElementById("detailBugError").textContent = "";

    renderComments();
}

async function handleSaveBugDetailClick() {
    const errorElement = document.getElementById("detailBugError");
    errorElement.textContent = "";
    const bug = State.activeBug;
    if (bug === null) {
        return;
    }

    const title = document.getElementById("detailBugTitle").value.trim();
    const description = document.getElementById("detailBugDescription").value;
    const status = document.getElementById("detailBugStatus").value;
    const priority = document.getElementById("detailBugPriority").value;
    const assigneeRaw = document.getElementById("detailBugAssignee").value;
    const projectRaw = document.getElementById("detailBugProject").value;
    const foundInRaw = document.getElementById("detailBugFoundInVersion").value;
    const fixedInRaw = document.getElementById("detailBugFixedInVersion").value;

    let assignedTo = 0;
    if (assigneeRaw) {
        assignedTo = Number(assigneeRaw);
    }
    let projectId = 0;
    if (projectRaw) {
        projectId = Number(projectRaw);
    }
    let foundInVersionId = 0;
    if (foundInRaw) {
        foundInVersionId = Number(foundInRaw);
    }
    let fixedInVersionId = 0;
    if (fixedInRaw) {
        fixedInVersionId = Number(fixedInRaw);
    }

    if (!title) {
        errorElement.textContent = "Title is required.";
        return;
    }
    if (projectId === 0) {
        errorElement.textContent = "Project is required.";
        return;
    }

    const updatePayload = {
        Title: title,
        Description: description,
        Status: status,
        UpdateStatus: true,
        Priority: priority,
        UpdatePriority: true,
        ClearAssignee: assignedTo === 0,
        AssignedTo: assignedTo,
        ProjectId: projectId,
        ClearFoundInVersion: foundInVersionId === 0,
        FoundInVersionId: foundInVersionId,
        ClearFixedInVersion: fixedInVersionId === 0,
        FixedInVersionId: fixedInVersionId
    };

    try {
        await apiRequest("PUT", "/api/bugs/" + bug.Id, updatePayload);
        await returnFromBugDetail();
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

function handleCancelBugDetailClick() {
    returnFromBugDetail();
}

function renderComments() {
    const commentsList = document.getElementById("commentsList");
    commentsList.innerHTML = "";

    if (State.comments.length === 0) {
        const emptyDiv = document.createElement("div");
        emptyDiv.className = "empty-message";
        emptyDiv.textContent = "No comments yet.";
        commentsList.appendChild(emptyDiv);
        return;
    }

    let commentIndex = 0;
    while (commentIndex < State.comments.length) {
        const comment = State.comments[commentIndex];
        const commentDiv = document.createElement("div");
        commentDiv.className = "comment";
        commentDiv.innerHTML =
            "<div class=\"comment-header\">" +
                "<span class=\"comment-author\">" + escapeHtml(comment.DisplayName) + "</span>" +
                "<span>" + escapeHtml(formatTimestamp(comment.CreatedAt)) + "</span>" +
            "</div>" +
            "<div class=\"comment-text\">" + escapeHtml(comment.Text) + "</div>";
        commentsList.appendChild(commentDiv);
        commentIndex++;
    }
}

async function handleNewCommentSubmit(submitEvent) {
    submitEvent.preventDefault();
    const textArea = document.getElementById("newCommentText");
    const text = textArea.value.trim();
    if (text.length === 0) {
        return;
    }
    try {
        await apiRequest("POST", "/api/bugs/" + State.activeBug.Id + "/comments", { Text: text });
        textArea.value = "";
        State.comments = await apiRequest("GET", "/api/bugs/" + State.activeBug.Id + "/comments");
        renderComments();
    } catch (apiError) {
        alert(apiError.message);
    }
}

async function handleNewBugClick() {
    State.activeBug = null;
    State.bugDetailReturnTo = "browseView";
    document.getElementById("bugEditHeading").textContent = "New bug";
    document.getElementById("bugEditSubmit").textContent = "Create bug";
    document.getElementById("bugTitle").value = "";
    document.getElementById("bugDescription").value = "";
    populateOptionSelect(document.getElementById("bugStatus"), State.metadata.Statuses, null, State.metadata.DefaultStatus);
    populateOptionSelect(document.getElementById("bugPriority"), State.metadata.Priorities, null, State.metadata.DefaultPriority);
    document.getElementById("bugAssignee").value = "";
    await loadProjects();
    fillProjectSelect("bugProject", true, null);
    fillVersionSelect("bugFoundInVersion", [], null);
    document.getElementById("bugStatus").parentElement.style.display = "none";
    document.getElementById("bugEditError").textContent = "";
    showView("bugEditView");
}

async function handleBugEditSubmit(submitEvent) {
    submitEvent.preventDefault();
    const errorElement = document.getElementById("bugEditError");
    errorElement.textContent = "";

    const title = document.getElementById("bugTitle").value.trim();
    const description = document.getElementById("bugDescription").value;
    const priority = document.getElementById("bugPriority").value;
    const assigneeRaw = document.getElementById("bugAssignee").value;
    const projectRaw = document.getElementById("bugProject").value;
    const foundInRaw = document.getElementById("bugFoundInVersion").value;
    let assignedTo = 0;
    if (assigneeRaw) {
        assignedTo = Number(assigneeRaw);
    }
    let projectId = 0;
    if (projectRaw) {
        projectId = Number(projectRaw);
    }
    let foundInVersionId = 0;
    if (foundInRaw) {
        foundInVersionId = Number(foundInRaw);
    }

    if (projectId === 0) {
        errorElement.textContent = "Project is required.";
        return;
    }

    try {
        const payload = {
            Title: title,
            Description: description,
            Priority: priority,
            AssignedTo: assignedTo,
            ProjectId: projectId,
            FoundInVersionId: foundInVersionId,
            FixedInVersionId: 0
        };
        const createdBug = await apiRequest("POST", "/api/bugs", payload);
        await openBugDetail(createdBug.Id);
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

function handleCancelBugEditClick() {
    if (State.activeBug) {
        showView("bugDetailView");
    } else {
        returnFromBugDetail();
    }
}

async function handleBackToListClick() {
    await returnFromBugDetail();
}

async function returnFromBugDetail() {
    State.activeBug = null;
    const target = State.bugDetailReturnTo;
    if (target === "userView") {
        showView("userView");
        await refreshUserView();
        return;
    }
    if (target === "browseView") {
        showView("browseView");
        await loadBrowseSection();
        return;
    }
    showView("homeView");
    await refreshHomeView();
}

async function handleNavHomeClick() {
    State.bugDetailReturnTo = "homeView";
    showView("homeView");
    await refreshHomeView();
}

async function handleNavBrowseClick() {
    State.bugDetailReturnTo = "browseView";
    showView("browseView");
    await loadUsers();
    await loadBrowseSection();
}

async function handleNavMyBugsClick() {
    State.bugDetailReturnTo = "userView";
    showView("userView");
    await refreshUserView();
}

async function handleNavSettingsClick() {
    showView("settingsView");
    document.getElementById("newUserPanel").classList.add("hidden");
    document.getElementById("editUserPanel").classList.add("hidden");
    document.getElementById("newProjectPanel").classList.add("hidden");
    document.getElementById("editProjectPanel").classList.add("hidden");
    document.getElementById("versionsPanel").classList.add("hidden");
    document.getElementById("newVersionPanel").classList.add("hidden");
    document.getElementById("editVersionPanel").classList.add("hidden");
    document.getElementById("newApiKeyPanel").classList.add("hidden");
    document.getElementById("apiKeyRevealPanel").classList.add("hidden");
    State.activeProjectIdForVersions = 0;
    await loadUsers();
    renderUserTable();
    await loadProjects();
    renderProjectTable();
    await loadApiKeys();
    renderApiKeyTable();
}

async function loadApiKeys() {
    State.apiKeys = await apiRequest("GET", "/api/api-keys");
}

function renderApiKeyTable() {
    const tableBody = document.getElementById("apiKeyTableBody");
    const emptyElement = document.getElementById("apiKeyEmpty");
    tableBody.innerHTML = "";

    if (State.apiKeys.length === 0) {
        emptyElement.classList.remove("hidden");
        return;
    }
    emptyElement.classList.add("hidden");

    const keyCount = State.apiKeys.length;
    for (let keyIndex = 0; keyIndex < keyCount; keyIndex++) {
        const apiKey = State.apiKeys[keyIndex];
        const row = document.createElement("tr");
        let lastUsedText = "(never)";
        if (apiKey.LastUsedAt) {
            lastUsedText = formatTimestamp(apiKey.LastUsedAt);
        }
        let actionCell = "";
        if (State.currentUser.IsAdmin) {
            actionCell = "<button type=\"button\" class=\"delete-button delete-api-key-button\" data-api-key-id=\"" + escapeHtml(apiKey.Id) + "\">Revoke</button>";
        }
        row.innerHTML =
            "<td>" + escapeHtml(apiKey.Name) + "</td>" +
            "<td>" + escapeHtml(apiKey.UserDisplayName) + " (" + escapeHtml(apiKey.UserUsername) + ")</td>" +
            "<td><code>" + escapeHtml(apiKey.KeyPrefix) + "&hellip;</code></td>" +
            "<td>" + escapeHtml(formatTimestamp(apiKey.CreatedAt)) + "</td>" +
            "<td>" + escapeHtml(lastUsedText) + "</td>" +
            "<td>" + actionCell + "</td>";
        tableBody.appendChild(row);
    }

    const deleteButtons = document.querySelectorAll(".delete-api-key-button");
    const deleteButtonCount = deleteButtons.length;
    for (let buttonIndex = 0; buttonIndex < deleteButtonCount; buttonIndex++) {
        deleteButtons[buttonIndex].addEventListener("click", handleDeleteApiKeyClick);
    }
}

function handleNewApiKeyButtonClick() {
    document.getElementById("apiKeyRevealPanel").classList.add("hidden");
    document.getElementById("newApiKeyPanel").classList.remove("hidden");
    document.getElementById("newApiKeyName").value = "";
    document.getElementById("newApiKeyError").textContent = "";

    const userSelect = document.getElementById("newApiKeyUser");
    userSelect.innerHTML = "";
    const placeholder = document.createElement("option");
    placeholder.value = "";
    placeholder.textContent = "(select a user)";
    userSelect.appendChild(placeholder);
    const userCount = State.users.length;
    for (let userIndex = 0; userIndex < userCount; userIndex++) {
        const user = State.users[userIndex];
        const optionElement = document.createElement("option");
        optionElement.value = String(user.Id);
        optionElement.textContent = user.DisplayName + " (" + user.Username + ")";
        userSelect.appendChild(optionElement);
    }
}

function handleCancelNewApiKeyClick() {
    document.getElementById("newApiKeyPanel").classList.add("hidden");
}

async function handleNewApiKeySubmit(submitEvent) {
    submitEvent.preventDefault();
    const errorElement = document.getElementById("newApiKeyError");
    errorElement.textContent = "";
    const name = document.getElementById("newApiKeyName").value.trim();
    const userRaw = document.getElementById("newApiKeyUser").value;
    if (!name) {
        errorElement.textContent = "Name is required.";
        return;
    }
    if (!userRaw) {
        errorElement.textContent = "Owner is required.";
        return;
    }
    const userId = Number(userRaw);
    try {
        const created = await apiRequest("POST", "/api/api-keys", { Name: name, UserId: userId });
        document.getElementById("newApiKeyPanel").classList.add("hidden");
        document.getElementById("apiKeyRevealValue").textContent = created.Key;
        document.getElementById("apiKeyRevealPanel").classList.remove("hidden");
        await loadApiKeys();
        renderApiKeyTable();
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

function handleApiKeyRevealDoneClick() {
    document.getElementById("apiKeyRevealPanel").classList.add("hidden");
    document.getElementById("apiKeyRevealValue").textContent = "";
}

async function handleDeleteApiKeyClick(clickEvent) {
    const button = clickEvent.currentTarget;
    const apiKeyId = Number(button.dataset.apiKeyId);
    const confirmed = window.confirm("Revoke this API key? Any external tool using it will lose access.");
    if (!confirmed) {
        return;
    }
    try {
        await apiRequest("DELETE", "/api/api-keys/" + apiKeyId);
        await loadApiKeys();
        renderApiKeyTable();
    } catch (apiError) {
        alert(apiError.message);
    }
}

function renderProjectTable() {
    const tableBody = document.getElementById("projectTableBody");
    tableBody.innerHTML = "";

    const projectCount = State.projects.length;
    for (let projectIndex = 0; projectIndex < projectCount; projectIndex++) {
        const project = State.projects[projectIndex];
        const row = document.createElement("tr");
        let actionCell = "";
        if (State.currentUser.IsAdmin) {
            actionCell = "<button type=\"button\" class=\"edit-button manage-versions-button\" data-project-id=\"" + escapeHtml(project.Id) + "\">Versions</button>";
            actionCell = actionCell + " <button type=\"button\" class=\"edit-button edit-project-button\" data-project-id=\"" + escapeHtml(project.Id) + "\">Edit</button>";
            actionCell = actionCell + " <button type=\"button\" class=\"delete-button delete-project-button\" data-project-id=\"" + escapeHtml(project.Id) + "\">Delete</button>";
        }
        row.innerHTML =
            "<td>" + escapeHtml(project.Name) + "</td>" +
            "<td>" + escapeHtml(project.VersionCount) + "</td>" +
            "<td>" + escapeHtml(formatTimestamp(project.CreatedAt)) + "</td>" +
            "<td>" + actionCell + "</td>";
        tableBody.appendChild(row);
    }

    const versionsButtons = document.querySelectorAll(".manage-versions-button");
    const versionsButtonCount = versionsButtons.length;
    for (let buttonIndex = 0; buttonIndex < versionsButtonCount; buttonIndex++) {
        versionsButtons[buttonIndex].addEventListener("click", handleManageVersionsClick);
    }
    const editProjectButtons = document.querySelectorAll(".edit-project-button");
    const editProjectButtonCount = editProjectButtons.length;
    for (let buttonIndex = 0; buttonIndex < editProjectButtonCount; buttonIndex++) {
        editProjectButtons[buttonIndex].addEventListener("click", handleEditProjectClick);
    }
    const deleteProjectButtons = document.querySelectorAll(".delete-project-button");
    const deleteProjectButtonCount = deleteProjectButtons.length;
    for (let buttonIndex = 0; buttonIndex < deleteProjectButtonCount; buttonIndex++) {
        deleteProjectButtons[buttonIndex].addEventListener("click", handleDeleteProjectClick);
    }
}

function findProjectById(projectId) {
    const projectCount = State.projects.length;
    for (let projectIndex = 0; projectIndex < projectCount; projectIndex++) {
        if (State.projects[projectIndex].Id === projectId) {
            return State.projects[projectIndex];
        }
    }
    return null;
}

function handleNewProjectButtonClick() {
    document.getElementById("editProjectPanel").classList.add("hidden");
    document.getElementById("newProjectPanel").classList.remove("hidden");
    document.getElementById("newProjectName").value = "";
    document.getElementById("newProjectError").textContent = "";
}

function handleCancelNewProjectClick() {
    document.getElementById("newProjectPanel").classList.add("hidden");
}

async function handleNewProjectSubmit(submitEvent) {
    submitEvent.preventDefault();
    const errorElement = document.getElementById("newProjectError");
    errorElement.textContent = "";
    const name = document.getElementById("newProjectName").value.trim();
    if (!name) {
        errorElement.textContent = "Name is required.";
        return;
    }
    try {
        await apiRequest("POST", "/api/projects", { Name: name });
        document.getElementById("newProjectPanel").classList.add("hidden");
        await loadProjects();
        renderProjectTable();
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

function handleEditProjectClick(clickEvent) {
    const button = clickEvent.currentTarget;
    const projectId = Number(button.dataset.projectId);
    const project = findProjectById(projectId);
    if (project === null) {
        return;
    }
    document.getElementById("newProjectPanel").classList.add("hidden");
    const editPanel = document.getElementById("editProjectPanel");
    editPanel.classList.remove("hidden");
    editPanel.dataset.projectId = String(projectId);
    document.getElementById("editProjectHeading").textContent = "Edit project: " + project.Name;
    document.getElementById("editProjectName").value = project.Name;
    document.getElementById("editProjectError").textContent = "";
}

function handleCancelEditProjectClick() {
    document.getElementById("editProjectPanel").classList.add("hidden");
}

async function handleEditProjectSubmit(submitEvent) {
    submitEvent.preventDefault();
    const errorElement = document.getElementById("editProjectError");
    errorElement.textContent = "";
    const editPanel = document.getElementById("editProjectPanel");
    const projectId = Number(editPanel.dataset.projectId);
    const name = document.getElementById("editProjectName").value.trim();
    if (!name) {
        errorElement.textContent = "Name is required.";
        return;
    }
    try {
        await apiRequest("PUT", "/api/projects/" + projectId, { Name: name });
        editPanel.classList.add("hidden");
        await loadProjects();
        renderProjectTable();
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

async function handleDeleteProjectClick(clickEvent) {
    const button = clickEvent.currentTarget;
    const projectId = Number(button.dataset.projectId);
    const confirmed = window.confirm("Delete this project?");
    if (!confirmed) {
        return;
    }
    try {
        await apiRequest("DELETE", "/api/projects/" + projectId);
        if (State.activeProjectIdForVersions === projectId) {
            State.activeProjectIdForVersions = 0;
            document.getElementById("versionsPanel").classList.add("hidden");
        }
        await loadProjects();
        renderProjectTable();
    } catch (apiError) {
        alert(apiError.message);
    }
}

async function handleManageVersionsClick(clickEvent) {
    const button = clickEvent.currentTarget;
    const projectId = Number(button.dataset.projectId);
    const project = findProjectById(projectId);
    if (project === null) {
        return;
    }
    State.activeProjectIdForVersions = projectId;
    document.getElementById("versionsHeading").textContent = "Versions of " + project.Name;
    document.getElementById("versionsPanel").classList.remove("hidden");
    document.getElementById("newVersionPanel").classList.add("hidden");
    document.getElementById("editVersionPanel").classList.add("hidden");
    await loadVersionsForActiveProject();
    renderVersionTable();
}

async function loadVersionsForActiveProject() {
    if (State.activeProjectIdForVersions === 0) {
        State.versionsForActiveProject = [];
        return;
    }
    State.versionsForActiveProject = await apiRequest("GET", "/api/projects/" + State.activeProjectIdForVersions + "/versions");
}

function renderVersionTable() {
    const tableBody = document.getElementById("versionTableBody");
    const emptyElement = document.getElementById("versionsEmpty");
    tableBody.innerHTML = "";

    if (State.versionsForActiveProject.length === 0) {
        emptyElement.classList.remove("hidden");
        return;
    }
    emptyElement.classList.add("hidden");

    const versionCount = State.versionsForActiveProject.length;
    for (let versionIndex = 0; versionIndex < versionCount; versionIndex++) {
        const version = State.versionsForActiveProject[versionIndex];
        const row = document.createElement("tr");
        let actionCell = "";
        if (State.currentUser.IsAdmin) {
            actionCell = "<button type=\"button\" class=\"edit-button edit-version-button\" data-version-id=\"" + escapeHtml(version.Id) + "\">Edit</button>";
            actionCell = actionCell + " <button type=\"button\" class=\"delete-button delete-version-button\" data-version-id=\"" + escapeHtml(version.Id) + "\">Delete</button>";
        }
        row.innerHTML =
            "<td>" + escapeHtml(version.Name) + "</td>" +
            "<td>" + escapeHtml(formatTimestamp(version.CreatedAt)) + "</td>" +
            "<td>" + actionCell + "</td>";
        tableBody.appendChild(row);
    }

    const editVersionButtons = document.querySelectorAll(".edit-version-button");
    const editVersionButtonCount = editVersionButtons.length;
    for (let buttonIndex = 0; buttonIndex < editVersionButtonCount; buttonIndex++) {
        editVersionButtons[buttonIndex].addEventListener("click", handleEditVersionClick);
    }
    const deleteVersionButtons = document.querySelectorAll(".delete-version-button");
    const deleteVersionButtonCount = deleteVersionButtons.length;
    for (let buttonIndex = 0; buttonIndex < deleteVersionButtonCount; buttonIndex++) {
        deleteVersionButtons[buttonIndex].addEventListener("click", handleDeleteVersionClick);
    }
}

function findActiveVersionById(versionId) {
    const versionCount = State.versionsForActiveProject.length;
    for (let versionIndex = 0; versionIndex < versionCount; versionIndex++) {
        if (State.versionsForActiveProject[versionIndex].Id === versionId) {
            return State.versionsForActiveProject[versionIndex];
        }
    }
    return null;
}

function handleNewVersionButtonClick() {
    document.getElementById("editVersionPanel").classList.add("hidden");
    document.getElementById("newVersionPanel").classList.remove("hidden");
    document.getElementById("newVersionName").value = "";
    document.getElementById("newVersionError").textContent = "";
}

function handleCancelNewVersionClick() {
    document.getElementById("newVersionPanel").classList.add("hidden");
}

async function handleNewVersionSubmit(submitEvent) {
    submitEvent.preventDefault();
    const errorElement = document.getElementById("newVersionError");
    errorElement.textContent = "";
    const name = document.getElementById("newVersionName").value.trim();
    if (!name) {
        errorElement.textContent = "Name is required.";
        return;
    }
    if (State.activeProjectIdForVersions === 0) {
        return;
    }
    try {
        await apiRequest("POST", "/api/projects/" + State.activeProjectIdForVersions + "/versions", { Name: name });
        document.getElementById("newVersionPanel").classList.add("hidden");
        await loadVersionsForActiveProject();
        renderVersionTable();
        await loadProjects();
        renderProjectTable();
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

function handleEditVersionClick(clickEvent) {
    const button = clickEvent.currentTarget;
    const versionId = Number(button.dataset.versionId);
    const version = findActiveVersionById(versionId);
    if (version === null) {
        return;
    }
    document.getElementById("newVersionPanel").classList.add("hidden");
    const editPanel = document.getElementById("editVersionPanel");
    editPanel.classList.remove("hidden");
    editPanel.dataset.versionId = String(versionId);
    document.getElementById("editVersionHeading").textContent = "Edit version: " + version.Name;
    document.getElementById("editVersionName").value = version.Name;
    document.getElementById("editVersionError").textContent = "";
}

function handleCancelEditVersionClick() {
    document.getElementById("editVersionPanel").classList.add("hidden");
}

async function handleEditVersionSubmit(submitEvent) {
    submitEvent.preventDefault();
    const errorElement = document.getElementById("editVersionError");
    errorElement.textContent = "";
    const editPanel = document.getElementById("editVersionPanel");
    const versionId = Number(editPanel.dataset.versionId);
    const name = document.getElementById("editVersionName").value.trim();
    if (!name) {
        errorElement.textContent = "Name is required.";
        return;
    }
    try {
        await apiRequest("PUT", "/api/projects/" + State.activeProjectIdForVersions + "/versions/" + versionId, { Name: name });
        editPanel.classList.add("hidden");
        await loadVersionsForActiveProject();
        renderVersionTable();
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

async function handleDeleteVersionClick(clickEvent) {
    const button = clickEvent.currentTarget;
    const versionId = Number(button.dataset.versionId);
    const confirmed = window.confirm("Delete this version?");
    if (!confirmed) {
        return;
    }
    try {
        await apiRequest("DELETE", "/api/projects/" + State.activeProjectIdForVersions + "/versions/" + versionId);
        await loadVersionsForActiveProject();
        renderVersionTable();
        await loadProjects();
        renderProjectTable();
    } catch (apiError) {
        alert(apiError.message);
    }
}

function renderUserTable() {
    const tableBody = document.getElementById("userTableBody");
    tableBody.innerHTML = "";

    let userIndex = 0;
    while (userIndex < State.users.length) {
        const user = State.users[userIndex];
        const row = document.createElement("tr");
        let adminText = "No";
        if (user.IsAdmin) {
            adminText = "Yes";
        }
        let actionCell = "";
        if (State.currentUser.IsAdmin) {
            actionCell = "<button type=\"button\" class=\"edit-button edit-user-button\" data-user-id=\"" + escapeHtml(user.Id) + "\">Edit</button>";
            if (user.Id !== State.currentUser.Id) {
                actionCell = actionCell + " <button type=\"button\" class=\"delete-button\" data-user-id=\"" + escapeHtml(user.Id) + "\">Delete</button>";
            }
        }
        row.innerHTML =
            "<td>" + escapeHtml(user.Username) + "</td>" +
            "<td>" + escapeHtml(user.DisplayName) + "</td>" +
            "<td>" + adminText + "</td>" +
            "<td>" + escapeHtml(formatTimestamp(user.CreatedAt)) + "</td>" +
            "<td>" + actionCell + "</td>";
        tableBody.appendChild(row);
        userIndex++;
    }

    const deleteButtons = document.querySelectorAll(".delete-button");
    let deleteButtonIndex = 0;
    while (deleteButtonIndex < deleteButtons.length) {
        deleteButtons[deleteButtonIndex].addEventListener("click", handleDeleteUserClick);
        deleteButtonIndex++;
    }

    const editButtons = document.querySelectorAll(".edit-user-button");
    let editButtonIndex = 0;
    while (editButtonIndex < editButtons.length) {
        editButtons[editButtonIndex].addEventListener("click", handleEditUserClick);
        editButtonIndex++;
    }
}

function findUserById(userId) {
    let searchIndex = 0;
    while (searchIndex < State.users.length) {
        if (State.users[searchIndex].Id === userId) {
            return State.users[searchIndex];
        }
        searchIndex++;
    }
    return null;
}

function handleEditUserClick(clickEvent) {
    const button = clickEvent.currentTarget;
    const userId = Number(button.dataset.userId);
    const user = findUserById(userId);
    if (user === null) {
        return;
    }

    document.getElementById("newUserPanel").classList.add("hidden");
    const editPanel = document.getElementById("editUserPanel");
    editPanel.classList.remove("hidden");
    editPanel.dataset.userId = String(userId);

    document.getElementById("editUserHeading").textContent = "Edit user: " + user.Username;
    document.getElementById("editUserDisplayName").value = user.DisplayName;
    document.getElementById("editUserPassword").value = "";
    document.getElementById("editUserPasswordConfirm").value = "";
    document.getElementById("editUserIsAdmin").checked = user.IsAdmin;
    document.getElementById("editUserError").textContent = "";
}

function handleCancelEditUserClick() {
    document.getElementById("editUserPanel").classList.add("hidden");
}

async function handleEditUserSubmit(submitEvent) {
    submitEvent.preventDefault();
    const errorElement = document.getElementById("editUserError");
    errorElement.textContent = "";

    const editPanel = document.getElementById("editUserPanel");
    const userId = Number(editPanel.dataset.userId);
    if (!userId) {
        return;
    }

    const displayNameValue = document.getElementById("editUserDisplayName").value.trim();
    const passwordValue = document.getElementById("editUserPassword").value;
    const passwordConfirmValue = document.getElementById("editUserPasswordConfirm").value;
    const isAdminValue = document.getElementById("editUserIsAdmin").checked;

    if (passwordValue.length > 0 || passwordConfirmValue.length > 0) {
        if (passwordValue !== passwordConfirmValue) {
            errorElement.textContent = "Passwords do not match.";
            return;
        }
    }

    const payload = {
        DisplayName: displayNameValue,
        IsAdmin: isAdminValue,
        UpdateIsAdmin: true
    };
    if (passwordValue.length > 0) {
        payload.Password = passwordValue;
    }

    try {
        await apiRequest("PUT", "/api/users/" + userId, payload);
        editPanel.classList.add("hidden");
        await loadUsers();
        renderUserTable();
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

async function handleDeleteUserClick(clickEvent) {
    const button = clickEvent.currentTarget;
    const userId = Number(button.dataset.userId);
    const confirmed = window.confirm("Delete this user?");
    if (!confirmed) {
        return;
    }
    try {
        await apiRequest("DELETE", "/api/users/" + userId);
        await loadUsers();
        renderUserTable();
    } catch (apiError) {
        alert(apiError.message);
    }
}

function handleNewUserButtonClick() {
    document.getElementById("editUserPanel").classList.add("hidden");
    document.getElementById("newUserPanel").classList.remove("hidden");
    document.getElementById("newUserUsername").value = "";
    document.getElementById("newUserDisplayName").value = "";
    document.getElementById("newUserPassword").value = "";
    document.getElementById("newUserPasswordConfirm").value = "";
    document.getElementById("newUserIsAdmin").checked = false;
    document.getElementById("newUserError").textContent = "";
}

async function handleNewUserSubmit(submitEvent) {
    submitEvent.preventDefault();
    const errorElement = document.getElementById("newUserError");
    errorElement.textContent = "";

    const passwordValue = document.getElementById("newUserPassword").value;
    const passwordConfirmValue = document.getElementById("newUserPasswordConfirm").value;
    if (passwordValue !== passwordConfirmValue) {
        errorElement.textContent = "Passwords do not match.";
        return;
    }

    const payload = {
        Username: document.getElementById("newUserUsername").value.trim(),
        DisplayName: document.getElementById("newUserDisplayName").value.trim(),
        Password: passwordValue,
        IsAdmin: document.getElementById("newUserIsAdmin").checked
    };
    try {
        await apiRequest("POST", "/api/users", payload);
        document.getElementById("newUserPanel").classList.add("hidden");
        await loadUsers();
        renderUserTable();
    } catch (apiError) {
        errorElement.textContent = apiError.message;
    }
}

async function onLoggedIn() {
    document.getElementById("currentUserLabel").textContent = State.currentUser.DisplayName;
    const settingsButton = document.getElementById("navSettingsButton");
    if (State.currentUser.IsAdmin) {
        settingsButton.classList.remove("hidden");
    } else {
        settingsButton.classList.add("hidden");
    }
    showHeader(true);
    await loadMetadata();
    await loadUsers();
    State.bugDetailReturnTo = "homeView";
    showView("homeView");
    await refreshHomeView();
}

async function bootstrap() {
    try {
        const sessionUser = await apiRequest("GET", "/api/auth/session");
        State.currentUser = sessionUser;
        await onLoggedIn();
    } catch (apiError) {
        showHeader(false);
        showView("loginView");
    }
}

function attachEventHandlers() {
    document.addEventListener("click", handleDocumentClickForDropdowns);
    document.getElementById("loginForm").addEventListener("submit", handleLoginSubmit);
    document.getElementById("logoutButton").addEventListener("click", handleLogoutClick);
    document.getElementById("navHomeButton").addEventListener("click", handleNavHomeClick);
    document.getElementById("navBrowseButton").addEventListener("click", handleNavBrowseClick);
    document.getElementById("navMyBugsButton").addEventListener("click", handleNavMyBugsClick);
    document.getElementById("navSettingsButton").addEventListener("click", handleNavSettingsClick);

    document.getElementById("homeNewStatusFilter").addEventListener("change", refreshHomeNewSection);
    document.getElementById("homeNewPriorityFilter").addEventListener("change", refreshHomeNewSection);
    document.getElementById("homeModifiedStatusFilter").addEventListener("change", refreshHomeModifiedSection);
    document.getElementById("homeModifiedPriorityFilter").addEventListener("change", refreshHomeModifiedSection);
    document.getElementById("homeUnassignedStatusFilter").addEventListener("change", refreshHomeUnassignedSection);
    document.getElementById("homeUnassignedPriorityFilter").addEventListener("change", refreshHomeUnassignedSection);

    document.getElementById("userCreatedStatusFilter").addEventListener("change", refreshUserCreatedSection);
    document.getElementById("userCreatedPriorityFilter").addEventListener("change", refreshUserCreatedSection);
    document.getElementById("userCreatedIncludeClosed").addEventListener("change", refreshUserCreatedSection);
    document.getElementById("userAssignedStatusFilter").addEventListener("change", refreshUserAssignedSection);
    document.getElementById("userAssignedPriorityFilter").addEventListener("change", refreshUserAssignedSection);
    document.getElementById("userAssignedIncludeClosed").addEventListener("change", refreshUserAssignedSection);

    document.getElementById("browseStatusFilter").addEventListener("change", loadBrowseSection);
    document.getElementById("browsePriorityFilter").addEventListener("change", loadBrowseSection);
    document.getElementById("browseAssigneeFilter").addEventListener("change", loadBrowseSection);
    document.getElementById("browseSortFilter").addEventListener("change", loadBrowseSection);

    document.getElementById("browseNewBugButton").addEventListener("click", handleNewBugClick);
    document.getElementById("backToListButton").addEventListener("click", handleBackToListClick);
    document.getElementById("bugProject").addEventListener("change", handleBugProjectChange);
    document.getElementById("detailBugProject").addEventListener("change", handleDetailBugProjectChange);
    document.getElementById("saveBugDetailButton").addEventListener("click", handleSaveBugDetailClick);
    document.getElementById("cancelBugDetailButton").addEventListener("click", handleCancelBugDetailClick);

    document.getElementById("newProjectButton").addEventListener("click", handleNewProjectButtonClick);
    document.getElementById("newProjectForm").addEventListener("submit", handleNewProjectSubmit);
    document.getElementById("cancelNewProjectButton").addEventListener("click", handleCancelNewProjectClick);
    document.getElementById("editProjectForm").addEventListener("submit", handleEditProjectSubmit);
    document.getElementById("cancelEditProjectButton").addEventListener("click", handleCancelEditProjectClick);
    document.getElementById("newVersionButton").addEventListener("click", handleNewVersionButtonClick);
    document.getElementById("newVersionForm").addEventListener("submit", handleNewVersionSubmit);
    document.getElementById("cancelNewVersionButton").addEventListener("click", handleCancelNewVersionClick);
    document.getElementById("editVersionForm").addEventListener("submit", handleEditVersionSubmit);
    document.getElementById("cancelEditVersionButton").addEventListener("click", handleCancelEditVersionClick);
    document.getElementById("newApiKeyButton").addEventListener("click", handleNewApiKeyButtonClick);
    document.getElementById("newApiKeyForm").addEventListener("submit", handleNewApiKeySubmit);
    document.getElementById("cancelNewApiKeyButton").addEventListener("click", handleCancelNewApiKeyClick);
    document.getElementById("apiKeyRevealDoneButton").addEventListener("click", handleApiKeyRevealDoneClick);
    document.getElementById("cancelBugEditButton").addEventListener("click", handleCancelBugEditClick);
    document.getElementById("bugEditForm").addEventListener("submit", handleBugEditSubmit);
    document.getElementById("newCommentForm").addEventListener("submit", handleNewCommentSubmit);

    document.getElementById("newUserButton").addEventListener("click", handleNewUserButtonClick);
    document.getElementById("newUserForm").addEventListener("submit", handleNewUserSubmit);
    document.getElementById("editUserForm").addEventListener("submit", handleEditUserSubmit);
    document.getElementById("cancelEditUserButton").addEventListener("click", handleCancelEditUserClick);
}

document.addEventListener("DOMContentLoaded", function onReady() {
    attachEventHandlers();
    bootstrap();
});
