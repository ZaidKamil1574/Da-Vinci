# Da Vinci Surgical Robot VR Trainer

A virtual reality training simulator for a da Vinci surgical robot, built in Unity 6 for the Meta Quest.

You stand in a hospital room next to a patient in bed. You can move the robot's arms by hand or from the surgeon's console, look through a camera mounted on one of the instruments, and swap surgical tools on the end of an arm. Several people can join the same room. This application is to showcase Kinematics with Unity Physics.

---

## Open the project in Unity

**You'll need**

- **Unity Hub**
- **Unity 6000.4.0f1** (Unity Hub will offer to install it if you don't have it)
- A few gigabytes of free disk space, because Unity builds a cache the first time it opens the project

**Steps**

1. At the top of this page, click the green **Code** button, then **Download ZIP**.
2. Unzip the file. You'll get a folder called **Da-Vinci-main**.
3. Open **Unity Hub**, click **Add**, then **Add project from disk**.
4. Choose the **Da-Vinci-main** folder.
5. Click the project in Unity Hub to open it. The first time takes several minutes while Unity imports everything.
6. In the **Project** window, go to **Assets › Scenes** and double-click **SampleScene**.
7. Press **Play**.

**Using a Quest?** Connect it to your computer with Quest Link before you press Play.

**No headset?** In the **Hierarchy**, click **XR Device Simulator**, then tick the checkbox next to its name at the top of the **Inspector**. Press Play and you can move around with the keyboard and mouse.

**Models missing, or the room looks untextured?** The download didn't include the large files (3D models, textures and sounds). Download the project with Git instead:

```bash
git lfs install
git clone https://github.com/ZaidKamil1574/Da-Vinci.git
```

Then add the new **Da-Vinci** folder in Unity Hub, starting from step 3.

---

## What you can do

- Grab one of the floating balls and the robot arm reaches for it.
- Use the levers and the dial on the surgeon's console to move the arm joints.
- Watch the live camera view from the tip of an instrument on the console screen.
- Pick up tweezers, clamps or a scalpel from the tray and attach them to the end of an arm.
- See live charts of how fast and how far each arm is moving.
- Press **Wake Up** to make the patient sit up in bed, and grab their hands to move them.
- Press the **A** button on the controller to pause everything.

---

## How it works

### Moving the robot arms

Each arm is a chain of joints. It uses Unity's Animation Rigging `ChainIKConstraint`, with one of the floating balls as its target. Every frame, the solver works out how to rotate each joint so the tip of the arm reaches the ball. It only rotates the joints, so the arm segments never stretch.

![Target ball IK goal tracking and FABRIK chain solving across the arm's joints](images/target-ball-ik.png)

The console can move the balls too (`DaVinciArmConsole`). If someone grabs a ball while the console is moving it, the console lets go so the two don't fight over it. A reset button on the console sends the balls back to where they started, and the arms follow.

### The levers and the dial

The lever and the dial work differently on purpose.

- **The lever** moves a joint at a steady speed while you hold it, and stops when you let go. That means you can park the arm anywhere, not just at the ends of its range. (`LeverDrivenRotator`)
- **The dial** sets the angle directly. Turn it halfway and the joint goes halfway. (`KnobDrivenRotator`)

![Lever rate control versus dial position control on the surgeon console](images/input-control.png)

In multiplayer, only the position of the lever or dial is sent over the network. Each player's computer works out the joint angle from that, so everyone always sees the arm in the same place.

### The endoscope camera

A camera sits on the tip of one instrument and moves with the arm. It draws what it sees onto a texture, and a screen at the surgeon's console shows that texture. (`EndoscopeCamera`)

![Endoscope camera on the arm tip streaming to the console monitor](images/endoscopic-view.png)

A few details that make it work:

- It can see things very close to the lens, down to 5 mm, like a real endoscope.
- It updates 30 times a second instead of every frame, to keep the Quest running smoothly.
- It can't see its own screen. Otherwise it would film the screen showing itself, and you'd get an endless tunnel of screens.

### Swapping tools

The end of the arm has a socket. Pick up a tool from the tray, bring it close, and it clicks into place and moves with the arm. Pull it back out to swap it for another one. The socket only accepts surgical tools, and a tool you drop that misses the socket falls to the floor. (`DaVinciInstrumentMount`, `DaVinciInstrument`)

![Instrument tray with modular coupling at the arm tip](images/tool-swapping.png)

### Charts and visual guides

- Live charts of each arm's speed and distance over time (`DaVinciTelemetryGraph`).
- A marker on each joint showing how far it has turned, in degrees.
- A line from each arm's tip to its target, and an arrow showing which way the tip is being pushed.

The charts and the joint markers use the same colours, so you can match a line on a chart to the joint it belongs to.

### The patient

The patient has two poses: asleep, and sitting up in bed. The **Resting** and **Wake Up** buttons next to the bed switch between them with a smooth animation. (`HumanoidPoseLibrary`)

You can also grab the patient's hands. The shoulder and elbow bend to follow your hand, so the arm moves naturally instead of stretching. (`GrabbableLimbIK`)

![Patient sleep-to-awake pose transition and grabbable limb IK](images/patient-ik.png)

### The hospital room

The hospital room asset was made for Unity's older render pipeline, so at first everything in it showed up bright pink. I converted all 70 of its materials to URP so it displays properly.

---

## Editor tools

Setting up this scene by hand means dragging a lot of references around a very deep hierarchy, so I wrote menu commands to do it. They're all safe to run again.

| Menu | What it does |
|---|---|
| **Tools › Da Vinci › Set Up Console And Angle Readouts** | Builds the console panel, the charts and the joint angle markers. |
| **Tools › Da Vinci › Set Up Endoscope View** | Puts the camera on the arm tip and builds the screen at the console. |
| **Tools › Da Vinci › Set Up Instrument Swapping** | Adds the socket to the arm tip and makes the tray tools grabbable. |
| **Tools › Da Vinci › Flip Instrument Mount** | Turns a tool around if it attaches upside down. |
| **Tools › Da Vinci › Patient Poses › Build Transition From Two Objects** | Turns two posed copies of a character into the asleep and awake animations. |
| **Tools › Da Vinci › Patient Poses › Wire Patient Buttons To This Character** | Connects the Resting and Wake Up buttons to the patient. |
| **Tools › Da Vinci › Patient Poses › Make Right / Left Hand Grabbable** | Lets you grab and move one of the patient's hands. |
| **GameObject › Da Vinci › Create Chain IK For Selection** | Sets up arm movement for the selected robot arm. |
| **GameObject › Da Vinci › Validate Rig Setup** | Checks the robot arms for missing or broken setup. |

---

## Where things are

```
Assets/
├── DaVinci/
│   ├── Scripts/        all of my code
│   └── Generated/      the patient animations and the endoscope texture
├── Scenes/SampleScene.unity
├── Hospital Room 2/    the hospital room
├── VR Body/            the patient characters
├── Da+Vinci.fbx        the robot
└── Robotic Surgery Controller.FBX    the surgeon's console
```

Inside `Assets/DaVinci/Scripts/`, the code is grouped by feature: `Controls`, `Endoscope`, `Instruments`, `Patient`, `UI`, `Visualization`, and `Editor` for the setup tools.

---

## Things I learned

- **Only one thing should control each object.** Most of the bugs I hit came from two systems moving the same bone at slightly different times, for example an animation and an IK constraint. The fix was always to decide which one is in charge.
- **Send the input, not the result.** In multiplayer, sending the lever position and letting each computer work out the arm's angle is simpler and more reliable than sending both.
- **Grab a target, not a bone.** Dragging a bone directly stretches the character's mesh. Grabbing a target and letting IK bend the arm to reach it looks right.
- **Look at the data before fixing anything.** The real causes usually turned up in the scene files or the animation data, not where I first guessed.

---

## Not finished yet

- I've written a custom arm solver (`CcdSolver` and `DaVinciArm`) that keeps each joint turning only on its real axis, and keeps the instrument pivoting around the incision point like the real robot does. It isn't connected to the scene yet, so the arms still use Unity's built-in solver.
- The instruments pass straight through the patient. There's no physical contact yet.
- Waking up is a blend between two poses rather than a fully animated movement.
- The real console scales down the surgeon's hand movements and filters out hand tremor. Neither is added yet.
- The robot model is in the scene twice, which doubles the work for the arm movement. One copy should be removed.

---

## Built with

Unity 6000.4.0f1 · Universal Render Pipeline 17.4 · OpenXR · XR Interaction Toolkit 3.4 · Animation Rigging 1.4 · Netcode for GameObjects 2.10

## Credits

This project uses assets made by other people, under their own licences:

- **Hospital room**: *Hospital Room 2* by 3D Everything, from the Unity Asset Store.
- **Patient characters**: Mixamo, by Adobe.
- **Multiplayer setup**: Unity's VR Multiplayer template and XR Interaction Toolkit samples.
- **VR body IK scripts** (`IKTargetFollowVRRig`, `IKFootSolver`): adapted from tutorial code.
- **Robot, console and surgical system models**: third-party 3D models, used under their original terms.

"Da Vinci" refers to the Intuitive Surgical system this project simulates for training. This project is not affiliated with Intuitive Surgical.
