function initLeadsGeoFilters() {
    const countrySelect = document.getElementById("countrySelect");
    const stateSelect = document.getElementById("stateSelect");
    const citySelect = document.getElementById("citySelect");
    if (!countrySelect || !stateSelect || !citySelect) return;
 
    const selectedCountryId = countrySelect.dataset.selected || "";
    const selectedStateId = stateSelect.dataset.selected || "";
    const selectedCityId = citySelect.dataset.selected || "";
    const hasTomSelect = typeof TomSelect !== 'undefined';
    let countryTS = null;
    let stateTS = null;
    let cityTS = null;
    if (hasTomSelect) {
        if (!countrySelect.tomselect) {
            countryTS = new TomSelect('#countrySelect', {
                allowEmptyOption: true,
                placeholder: "All countries",
                plugins: ['dropdown_input'],
            });
        } else {
            countryTS = countrySelect.tomselect;
        }
        if (!stateSelect.tomselect) {
            stateTS = new TomSelect('#stateSelect', {
                allowEmptyOption: true,
                placeholder: "Type to search states...",
                plugins: ['dropdown_input'],
                valueField: 'value',
                labelField: 'text',
                searchField: 'text',
                load: function (query, callback) {
                    const countryId = countrySelect.value;
                    if (!countryId) return callback();
                    fetch(`/api/geo/states?countryId=${countryId}&search=${encodeURIComponent(query)}`)
                        .then(r => r.json())
                        .then(data => callback(data.map(s => ({ value: String(s.id), text: s.name }))))
                        .catch(() => callback());
                }
            });
        } else {
            stateTS = stateSelect.tomselect;
        }
        if (!citySelect.tomselect) {
            cityTS = new TomSelect('#citySelect', {
                allowEmptyOption: true,
                placeholder: "Type to search cities...",
                plugins: ['dropdown_input'],
                valueField: 'value',
                labelField: 'text',
                searchField: 'text',
                load: function (query, callback) {
                    const stateId = stateSelect.value;
                    if (!stateId) return callback();
                    fetch(`/api/geo/cities?stateId=${stateId}&search=${encodeURIComponent(query)}`)
                        .then(r => r.json())
                        .then(data => callback(data.map(c => ({ value: String(c.id), text: c.name }))))
                        .catch(() => callback());
                }
            });
        } else {
            cityTS = citySelect.tomselect;
        }
    }
    function resetSelect(selectEl, placeholder) {
        if (hasTomSelect && selectEl.tomselect) {
            const ts = selectEl.tomselect;
            ts.clear(true);
            ts.clearOptions();
            ts.addOption({ value: "", text: placeholder });
            ts.refreshOptions(false);
        } else {
            selectEl.innerHTML = "";
            const opt = document.createElement("option");
            opt.value = "";
            opt.textContent = placeholder;
            selectEl.appendChild(opt);
        }
    }
    async function loadCountries() {
        resetSelect(countrySelect, "All countries");
        resetSelect(stateSelect, "All states / regions");
        resetSelect(citySelect, "All cities");
        if (!hasTomSelect) {
            stateSelect.disabled = true;
            citySelect.disabled = true;
        } else {
            stateTS.disable();
            cityTS.disable();
        }
        const response = await fetch("/api/geo/countries");
        const countries = await response.json();
        if (hasTomSelect && countrySelect.tomselect) {
            const ts = countrySelect.tomselect;
            countries.forEach(c => {
                ts.addOption({ value: String(c.id), text: c.name });
            });
            ts.refreshOptions(false);
            if (selectedCountryId) {
                ts.setValue(String(selectedCountryId), true);
                await loadStates(selectedCountryId, selectedStateId, selectedCityId);
            }
        } else {
            countries.forEach(c => {
                const opt = document.createElement("option");
                opt.value = c.id;
                opt.textContent = c.name;
                countrySelect.appendChild(opt);
            });
            if (selectedCountryId) {
                countrySelect.value = selectedCountryId;
                await loadStates(selectedCountryId, selectedStateId, selectedCityId);
            }
        }
    }
    async function loadStates(countryId, preselectStateId, preselectCityId) {
        resetSelect(stateSelect, "Type to search states...");
        resetSelect(citySelect, "Type to search cities...");
        if (!countryId) {
            if (!hasTomSelect) {
                stateSelect.disabled = true;
                citySelect.disabled = true;
            } else {
                stateTS.disable();
                cityTS.disable();
            }
            return;
        }
        if (hasTomSelect && stateSelect.tomselect) {
            const ts = stateSelect.tomselect;
            ts.clear(true);
            ts.clearOptions();
            ts.enable();
            if (preselectStateId) {
                const response = await fetch(`/api/geo/states?countryId=${countryId}&search=`);
                const states = await response.json();
                const found = states.find(s => String(s.id) === String(preselectStateId));
                if (found) {
                    ts.addOption({ value: String(found.id), text: found.name });
                    ts.setValue(String(found.id), true);
                }
                await loadCities(preselectStateId, preselectCityId);
            }
        } else {
            stateSelect.innerHTML = "";
            const opt = document.createElement("option");
            opt.value = "";
            opt.textContent = "Type to search states...";
            stateSelect.appendChild(opt);
            stateSelect.disabled = false;
        }
    }
    async function loadCities(stateId, preselectCityId) {
        resetSelect(citySelect, "Type to search cities...");
        if (!stateId) {
            if (!hasTomSelect) {
                citySelect.disabled = true;
            } else {
                cityTS.disable();
            }
            return;
        }
        if (hasTomSelect && citySelect.tomselect) {
            const ts = citySelect.tomselect;
            ts.clear(true);
            ts.clearOptions();
            ts.enable();
            if (preselectCityId) {
                const response = await fetch(`/api/geo/cities?stateId=${stateId}&search=`);
                const cities = await response.json();
                const found = cities.find(c => String(c.id) === String(preselectCityId));
                if (found) {
                    ts.addOption({ value: String(found.id), text: found.name });
                    ts.setValue(String(found.id), true);
                }
            }
        } else {
            citySelect.innerHTML = "";
            const opt = document.createElement("option");
            opt.value = "";
            opt.textContent = "Type to search cities...";
            citySelect.appendChild(opt);
            citySelect.disabled = false;
        }
    }
    countrySelect.addEventListener("change", () => {
        const id = countrySelect.value;
        loadStates(id, "", "");
    });
    stateSelect.addEventListener("change", () => {
        const id = stateSelect.value;
        loadCities(id, "");
    });
    loadCountries();  
};
function initNicheDropdown() {
    const nicheSelect = document.getElementById("nicheSelect");

    if (!nicheSelect) return;

    // Check if the dropdown has already been initialized


    const selectedNicheId = nicheSelect.dataset.selected || "";
    const hasTomSelect = typeof TomSelect !== 'undefined';
    let nicheTS = null;

    // Initialize TomSelect if not already initialized
    if (hasTomSelect) {
        if (!nicheSelect.tomselect) {
            nicheTS = new TomSelect('#nicheSelect', {
                allowEmptyOption: true,
                placeholder: "Select a niche",
                plugins: ['dropdown_input'],  // Allow searching/filtering within dropdown
            });
        } else {
            nicheTS = nicheSelect.tomselect;
        }
    }

    // Reset the dropdown and set the placeholder
    function resetSelect(selectEl, placeholder) {
        if (hasTomSelect && selectEl.tomselect) {
            const ts = selectEl.tomselect;
            ts.clear(true);
            ts.clearOptions();
            ts.addOption({ value: "", text: placeholder });
            ts.refreshOptions(false);
        } else {
            selectEl.innerHTML = "";
            const opt = document.createElement("option");
            opt.value = "";
            opt.textContent = placeholder;
            selectEl.appendChild(opt);
        }
    }

    // Fetch niches from the server (API call)
    async function loadNiches() {
        resetSelect(nicheSelect, "Select a niche");  // Reset and set a placeholder
        if (!hasTomSelect) {
            nicheSelect.disabled = true;
        } else {
            nicheTS.disable();
        }

        try {
            // Replace with the actual API endpoint for fetching niches
            const response = await fetch("/api/geo/niches");
            const niches = await response.json();

            if (response.ok) {
                // Populate the dropdown with the niches using TomSelect
                if (hasTomSelect && nicheSelect.tomselect) {
                    const ts = nicheSelect.tomselect;
                    niches.forEach(niche => {
                        ts.addOption({ value: niche.id, text: niche.name });
                    });
                    ts.refreshOptions(false);
                } else {
                    niches.forEach(niche => {
                        const option = document.createElement('option');
                        option.value = niche.id;
                        option.textContent = niche.name;
                        nicheSelect.appendChild(option);
                    });
                }


                // If there's a preselected niche, set it
                if (selectedNicheId) {
                    nicheSelect.value = selectedNicheId;
                    if (nicheTS) {
                        nicheTS.setValue(selectedNicheId); // For TomSelect instance
                    }
                }

                // Enable the dropdown if TomSelect is used
                if (hasTomSelect) {
                    nicheTS.enable();
                } else {
                    nicheSelect.disabled = false;
                }
            } else {
                console.error("Error fetching niches:", response.statusText);
            }
        } catch (error) {
            console.error("Error fetching niches:", error);
        }
    }

    // Call the function to load niches
    loadNiches();
}

const LEAD_STATUS = {
    New: 0,
    CalledNoAnswer: 1,
    LeftVoicemail: 2,
    Interested: 3,
    FollowUp: 4,
    EmailSent: 5,
    NotInterested: 6,
    WrongNumber: 7,
    Closed: 8
};
let gmCurrentLeadId = null;
let gmCurrentStatus = null;
const STATUS_CONFIG = {
    CalledNoAnswer: {
        id: LEAD_STATUS.CalledNoAnswer,
        title: "Called, No Answer",
        helper: "When did you try and how?",
        required: false
    },
    LeftVoicemail: {
        id: LEAD_STATUS.LeftVoicemail,
        title: "Left Voicemail",
        helper: "What message did you leave?",
        required: false
    },
    Interested: {
        id: LEAD_STATUS.Interested,
        title: "Mark as Interested",
        helper: "What is the requirement or next expectation?",
        required: true
    },
    FollowUp: {
        id: LEAD_STATUS.FollowUp,
        title: "Set Follow Up",
        helper: "What is the next step and when?",
        required: true
    },
    EmailSent: {
        id: LEAD_STATUS.EmailSent,
        title: "Email Sent",
        helper: "What was the email about?",
        required: false
    },
    NotInterested: {
        id: LEAD_STATUS.NotInterested,
        title: "Mark as Not Interested",
        helper: "Why was this lead rejected?",
        required: true
    },
    WrongNumber: {
        id: LEAD_STATUS.WrongNumber,
        title: "Wrong Number",
        helper: "Any clarification?",
        required: false
    },
    Closed: {
        id: LEAD_STATUS.Closed,
        title: "Mark as Closed",
        helper: "How was this deal closed?",
        required: true
    }
};
function openStatusNotes(leadId, statusKey) {
    gmCurrentLeadId = leadId;
    gmCurrentStatus = statusKey;
    const config = STATUS_CONFIG[statusKey];
    if (!config) return;
    document.getElementById("gmNotesTitle").textContent = config.title;
    document.getElementById("gmNotesHelper").textContent = config.helper;
    const textarea = document.getElementById("gmNotesTextarea");
    textarea.value = "";
    textarea.placeholder = config.helper;
    document.getElementById("gmNotesError").classList.add("d-none");
    const followUpWrapper = document.getElementById("gmFollowUpDateWrapper");
    if (followUpWrapper) {
        if (statusKey === 'FollowUp') {
            followUpWrapper.classList.remove("d-none");
            const dateInput = document.getElementById("gmFollowUpDate");
            if (dateInput) dateInput.value = "";
        } else {
            followUpWrapper.classList.add("d-none");
        }
    }
    document.getElementById("gmNotesBackdrop").classList.add("active");
    document.getElementById("gmNotesSheet").classList.add("active");
    document.body.classList.add("gm-scroll-locked");

    setTimeout(() => {
        textarea.focus({ preventScroll: true });
    }, 300);
}
function closeStatusNotes() {
    document.getElementById("gmNotesBackdrop")?.classList.remove("active");
    document.getElementById("gmNotesSheet")?.classList.remove("active");
    document.body.classList.remove("gm-scroll-locked");

}
document.addEventListener("DOMContentLoaded", function () {
    const closeBtn = document.getElementById("gmNotesClose");
    const cancelBtn = document.getElementById("gmNotesCancel");
    const backdrop = document.getElementById("gmNotesBackdrop");
    const saveBtn = document.getElementById("gmNotesSave");
    if (closeBtn) closeBtn.addEventListener("click", closeStatusNotes);
    if (cancelBtn) cancelBtn.addEventListener("click", closeStatusNotes);
    if (backdrop) backdrop.addEventListener("click", closeStatusNotes);
    if (saveBtn) {
        saveBtn.addEventListener("click", function () {
            const config = STATUS_CONFIG[gmCurrentStatus];
            if (!config) return;
            const notes = document.getElementById("gmNotesTextarea").value.trim();
            if (config.required && !notes) {
                document.getElementById("gmNotesError").classList.remove("d-none");
                return;
            }
            const followUpDateInput = document.getElementById("gmFollowUpDate");
            const followUpDate = (gmCurrentStatus === 'FollowUp' && followUpDateInput)
                ? followUpDateInput.value || null
                : null;
            fetch("/Leads/UpdateLeadStatus", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken":
                        document.getElementById("gmRequestVerificationToken")?.value || ""
                },              
                body: JSON.stringify({
                    userLeadId: gmCurrentLeadId,
                    status: STATUS_CONFIG[gmCurrentStatus].id,
                    notes: notes,
                    followUpDate: followUpDate
                })
            })
                .then(r => {
                    if (!r.ok) throw new Error();
                    closeStatusNotes();
                    location.reload();
                })
                .catch(() => alert("Failed to update status"));
        });
    }
});
document.addEventListener("DOMContentLoaded", function () {
    const drawer = document.getElementById("gmLeadDrawer");
    const backdrop = document.getElementById("gmDrawerBackdrop");
    const closeBtn = document.getElementById("gmDrawerClose");
    const body = document.getElementById("gmDrawerBody");
    //const title = document.getElementById("gmDrawerTitle");
    if (!drawer || !backdrop) {
        console.error("Drawer elements not found");
        return;
    }
    let gmScrollTop = 0;
    let gmDrawerLeadId = null;
    function openDrawer(leadId) {

        gmDrawerLeadId = leadId;
        gmScrollTop = window.scrollY || document.documentElement.scrollTop;

        document.body.classList.add("gm-scroll-locked");
        drawer.classList.add("active");
        backdrop.classList.add("active");
        //title.textContent = "Lead #" + leadId;
        body.innerHTML = "<div class='text-muted small'>Loading…</div>";
        fetch("/Leads/DetailsPartial?id=" + leadId)
            .then(r => r.text())
            .then(html => body.innerHTML = html)
            .catch(() => {
                body.innerHTML = "<div class='text-danger'>Failed to load details</div>";
            });
    }
    function closeDrawer() {
        drawer.classList.remove("active");
        backdrop.classList.remove("active");
        document.body.classList.remove("gm-scroll-locked");
        document.body.style.top = "";
        window.scrollTo(0, gmScrollTop);
    }
    document.addEventListener("click", function (e) {
        const btn = e.target.closest(".btn-view-details");
        if (!btn) return;

        e.preventDefault();

        e.stopPropagation();

        const leadId = btn.dataset.leadId;


        if (!leadId) return;
        openDrawer(leadId);
    });
    closeBtn?.addEventListener("click", closeDrawer);
    backdrop.addEventListener("click", closeDrawer);
});








document.addEventListener("DOMContentLoaded", () => {
    initLeadsTable();
});


function initLeadsTable() {
    const tableEl = document.querySelector("#leadsTable");
    const cardEl = document.querySelector(".leads-table-card");

    if (!tableEl) return null;

    // 🔥 Always destroy if already initialized
    if (DataTable.isDataTable(tableEl)) {
        const oldDt = DataTable.getInstance(tableEl);
        oldDt?.destroy();
    }

    const dt = new DataTable(tableEl, {
        pageLength: 25,
        lengthChange: true,
        info: true,
        searching: true,
        layout: {
            topStart: "pageLength",
            topEnd: null,
            bottomStart: "info",
            bottomEnd: "paging"
        },
        columnDefs: [
            { targets: 0, orderable: false, searchable: false }, // checkbox
            { targets: 1, orderable: false, searchable: false }, // serial #
            { targets: 8, orderable: false }                     // status
        ]
    });

    // Serial numbering — column 1 is the # column (column 0 is now the checkbox)
    dt.on("draw", function () {
        const pageInfo = dt.page.info();
        const start = pageInfo.start;

        dt.column(1, { page: "current" }).nodes().each((cell, i) => {
            cell.textContent = start + i + 1;
        });
    });

    dt.draw();

    tableEl.classList.add("dt-initialized");
    cardEl?.classList.add("dt-ready");

    const searchInput = document.querySelector('.filters-form input[name="search"]');
    if (searchInput) {
        const apply = debounce((val) => dt.search(val || "").draw(), 200);
        searchInput.addEventListener("input", (e) => apply(e.target.value));
    }

    return dt;
}



function debounce(fn, delay = 200) {
    let t;
    return (...args) => {
        clearTimeout(t);
        t = setTimeout(() => fn(...args), delay);
    };
}


function initSimpleDropdowns() {
    if (typeof TomSelect === 'undefined') return;
    const simpleDropdowns = document.querySelectorAll('.ts-simple');
    simpleDropdowns.forEach(function (el) {
        if (el.tomselect) return;
        new TomSelect(el, {
            maxItems: 1,
            allowEmptyOption: true,
            create: false,
            searchField: [],
            controlInput: null,
            plugins: [],
            render: {
                option_create: () => null
            }
        });
    });
}
