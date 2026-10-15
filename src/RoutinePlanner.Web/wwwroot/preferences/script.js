function getAbbreviation(str) {
  const stopWords = ["of", "and"];
  const words = str.split(" ");
  let abbreviation = "";

  for (let word of words) {
    if (word === word.toUpperCase() && word.length > 1) {
      abbreviation += word;
    } else if (!stopWords.includes(word.toLowerCase())) {
      abbreviation += word.charAt(0).toUpperCase();
    }
  }

  return abbreviation;
}

function generateSubjects(semesterNumber) {
  const sectionContainer = document.querySelector("#subjects");

  const semester = courses.find((c) => c.semester === semesterNumber);
  if (!semester) return;

  const section = document.createElement("section");

  const heading = document.createElement("h4");
  heading.style.fontSize = "0.95rem";
  heading.style.fontWeight = "normal";
  heading.style.margin = "0 0 0.5rem 2ch";
  heading.style.textDecoration = "underline";
  heading.textContent = `Semester ${semesterNumber}`;
  section.appendChild(heading);

  semester.subjects.forEach((subject) => {
    const shortName = getAbbreviation(subject.name);

    const label = document.createElement("label");
    label.style.fontSize = "0.9rem";
    label.style.display = "block";

    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.name = "subjects";
    checkbox.value = subject.code;
    checkbox.checked = subject.type === "compulsory";

    label.appendChild(checkbox);
    label.appendChild(
      // document.createTextNode(` ${subject.code} [${shortName}]`)
      document.createTextNode(subject.name)
    );
    label.title = subject.name;

    section.appendChild(label);
  });

  sectionContainer.appendChild(section);
}

function semesterToggle() {
  const checkedSections = document.querySelectorAll(
    'input[name="sections"]:checked'
  );
  const sectionContainer = document.querySelector("#subjects");
  sectionContainer.innerHTML = "";

  const semesters = new Set();

  checkedSections.forEach((checkbox) => {
    const semesterNum = parseInt(checkbox.value.charAt(0), 10);
    semesters.add(semesterNum);
  });

  semesters.forEach((semesterNum) => {
    generateSubjects(semesterNum);
  });
}

function generateSections(OddEven) {
  const sectionContainer = document.querySelector("#year");
  sectionContainer.innerHTML = "";

  for (let column = 1; column <= 4; column++) {
    ["A", "B"].forEach((section) => {
      const label = document.createElement("label");
      label.style.fontSize = "0.9rem";
      label.style.display = "inline-block";
      label.style.marginRight = "1rem";

      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.name = "sections";
      checkbox.value = `${column}${section}`;
      checkbox.checked =
        (OddEven === "Odd" && column % 2 === 1) ||
        (OddEven === "Even" && column % 2 === 0);

      checkbox.addEventListener("change", semesterToggle);

      label.appendChild(checkbox);
      label.appendChild(document.createTextNode(` ${column}${section}`));
      sectionContainer.appendChild(label);
    });
  }

  semesterToggle();
}
