let teacherData = {
  salutation: "",
  name: "",
  subjects_array: [],
  availability_json: {},
};

document.addEventListener("DOMContentLoaded", () => {
  const salutation = document.querySelector('input[name="salutation"]');
  salutation.addEventListener("change", () => {
    teacherData.salutation = salutation.value;
  });

  const fullName = document.querySelector('input[name="fullName"]');
  fullName.addEventListener("change", () => {
    teacherData.name = fullName.value;
  });

  document.forms["teacher"].addEventListener("submit", async (event) => {
    event.preventDefault();

    if (!teacherData.name || teacherData.name.trim() === "") {
      alert("Teacher's name cannot be empty!");
      return;
    }

    const sanitizedAvailability = {};
    for (const [day, slots] of Object.entries(teacherData.availability_json)) {
      sanitizedAvailability[day] = slots.map(({ start, end }) => ({
        start,
        end,
      }));
    }

    const payload = {
      salutation: teacherData.salutation,
      name: teacherData.name,
      subjects_array: teacherData.subjects_array,
      availability_json: sanitizedAvailability,
    };

    try {
      await addTeacher(payload);
      alert("Teacher added successfully!");
      window.location.href = window.location.origin;
    } catch (err) {
      alert("Error adding teacher: " + err.message);
    }
  });
});

async function addTeacher(teacher) {
  const res = await fetch("/teacher/add", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(teacher),
  });
  if (!res.ok) throw new Error("Failed to add teacher");
  return res.json();
}
