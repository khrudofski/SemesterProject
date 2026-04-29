import React, { useEffect, useState } from 'react'
import { API_BASE } from './api'
import { useRef } from 'react' // for the message timer
// Asset images used in the UI
import logo from './assets/logo.png'
import mapImage from './assets/map.png'
import runnerBg from './assets/runner-bg.jpg'
import homepageRunners from './assets/homepage-runners.jpg'
import {
  getAdminRunners,
  getMyProfile,
  getMyTeammates,
  getPublicCharity,
  getPublicRace,
  getPublicSchedule,
  login,
  registerRunner,
} from './api'

const pages = ['Home', 'Login', 'Register', 'Dashboard', 'Admin']

export default function App() {
  // tracks the current visible page
  const [page, setPage] = useState('Home')
  // authenticates token stored in localStorage
  const [token, setToken] = useState(localStorage.getItem('token') || '')
  // global ui message system for success and error feedback
  const [message, setMessage] = useState('')
  // used to clear messages after a delay
  const messageTimerRef = useRef(null)
  // public data from the backend
  const [charity, setCharity] = useState(null)
  const [race, setRace] = useState(null)
  const [schedule, setSchedule] = useState([])
  // controls the map lightbox/modal
  const [showMap, setShowMap] = useState(false)
  // logged in user data
  const [profile, setProfile] = useState(null)
  const [teammates, setTeammates] = useState([])
  const [adminRunners, setAdminRunners] = useState([])

  // form states 
  const [loginForm, setLoginForm] = useState({ email: '', password: '' })
  const [registerForm, setRegisterForm] = useState({
    name: '',
    email: '',
    age: '',
    emergencyPhone: '',
    password: '',
    confirmPassword: '',
    teamName: '',
  })

  useEffect(() => {
    loadPublicData()
  }, [])

  useEffect(() => {
    if (token) {
      loadPrivateData(token)
    } else {
      setProfile(null)
      setTeammates([])
      setAdminRunners([])
    }
  }, [token])

  // shows a temporary message for success/error notifications
  function showMessage(text, duration = 3000) {
      setMessage(text)

      // clears the previous timer so messages don't overlap
      if (messageTimerRef.current) {
          clearTimeout(messageTimerRef.current)
      }

      // auto-clears the message after a delay
      messageTimerRef.current = setTimeout(() => {
          setMessage('')
      }, duration)
  }

  // prevent unauthorised users from accessing restricted pages
  useEffect(() => {
      if (!token && (page === 'Dashboard' || page === 'Admin')) {
          setMessage('Please log in to access this page.')
          setPage('Login')
      }
  }, [token, page])

  // loads data visible to all users
  async function loadPublicData() {
    const [charityData, raceData, scheduleData] = await Promise.all([
      getPublicCharity(),
      getPublicRace(),
      getPublicSchedule(),
    ])

    setCharity(charityData)
    setRace(raceData)
    setSchedule(scheduleData)
  }

  // loads data for logged-in users only
  async function loadPrivateData(currentToken) {
    const me = await getMyProfile(currentToken)
    setProfile(me)

    const mates = await getMyTeammates(currentToken)
    setTeammates(Array.isArray(mates) ? mates : [])

    // admin specific access to runner management
    if (me?.isAdmin) {
      const runners = await getAdminRunners(currentToken)
      setAdminRunners(Array.isArray(runners) ? runners : [])
    }
  }

// Enables an admin to make another user an admin
async function makeAdmin(id) {
  const confirmAction = window.confirm(
    "Are you sure you want to make this user an admin?"
  );
  if (!confirmAction) return;

  try {
    const res = await fetch(`${API_BASE}/api/admin/runners/${id}/make-admin` ,
      {
        method: "POST",
        headers: {
          Authorization: `Bearer ${token}`,
        },
      }
    );

    // Handle empty responses
    let data = {};
    try {
        data = await res.json();
    } catch {
        data = {};
    }

    if (!res.ok) {
        showMessage(data.message || "Failed to make admin");
        return;
    }

    showMessage(data.message || "User promoted to admin");

    // refresh admin list properly
    const updated = await getAdminRunners(token);
    setAdminRunners(updated);

  } catch (err) {
    console.error(err);
    showMessage("Something went wrong while updating admin.");
  }
}

// Handles removal of Admin privileges
async function removeAdmin(id) {
    const confirmAction = window.confirm(
        "Are you sure you want to remove admin privileges from this user?"
    );
    if (!confirmAction) return;

    try { 
        const res = await fetch(`${API_BASE}/api/admin/runners/${id}/remove-admin`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                Authorization: `Bearer ${token}`
            }
        });
    
    let data = {};
    try {
        data = await res.json();
    } catch { data = {};
    }

    if (!res.ok) {
        showMessage(data.error || "Failed to remove admin");
    }

    showMessage("Admin privileges removed.");

    // refreshes list after the change
    const updated = await getAdminRunners(token);
    setAdminRunners(updated);
    }
    catch (err) {
        console.error(err);
        showMessage("Something went wrong.");
    }
}

// prevents users from submitting information twice
const [isLoading, setIsLoading] = useState(false)

// handles the user login and token storage
async function handleLogin(event) {
  event.preventDefault()
  showMessage('')

  // validation for login information
  if (!loginForm.email || !loginForm.password) {
    showMessage('Please enter both email and password.')
    return
  }

  try {
    setIsLoading(true)

    const result = await login(
      loginForm.email.trim(),
      loginForm.password
    )

    if (!result || result.error) {
      showMessage(result?.error || 'Login failed.')
      return
    }

    if (!result.token) {
        showMessage('User not found.')
        return
    }

    localStorage.setItem('token', result.token)
    setToken(result.token)

    // successful login confirmation message
    showMessage('Login successful! Redirecting...')

    // delay for better feedback
    setTimeout(() => {
      setPage('Dashboard')
    }, 800)

  } catch (err) {
    console.error("Login error:", err)
    showMessage('Something went wrong. Please try again.')
  } finally {
    setIsLoading(false)
  }
}
  // handles the user registration 
  async function handleRegister(event) {
    event.preventDefault()
    showMessage('')

    // Frontend validation
    if (!registerForm.name.trim()) {
        showMessage('Name is required.')
        return
    }

    if (!registerForm.email.includes('@')) {
        showMessage('Please enter a valid email.')
        return
    }

    if (!registerForm.password || registerForm.password.length < 5) {
        showMessage('Password must be at least 5 characters.')
        return
    }

    if (registerForm.password !== registerForm.confirmPassword) {
        showMessage("Passwords do not match");
        return;
}

    if (!registerForm.age || isNaN(registerForm.age)) {
        showMessage('Please enter a valid age.')
        return
    }

    if (!registerForm.emergencyPhone.trim()) {
        showMessage('Emergency phone is required.')
        return
    }

    // Backend data
    const payload = {
      ...registerForm,
      age: Number(registerForm.age),
    }

    // Send request
    const result = await registerRunner(payload)

    // backend errors
    if (result.error) {
      showMessage(result.error)
      return
    }

    // successful feedback
    showMessage('Registration successful! You can now log in.')

    // clear the form
    setRegisterForm({
        name: '',
        email: '',
        age: '',
        emergencyPhone: '',
        password: '',
        teamName: '',
        confirmPassword: '',
    })

    // redirects to the login after a short delay
    setTimeout(() => {
        setPage('Login')
  }, 1000)
  }

  //  clears the session and resets the state
  function logout() {
    localStorage.removeItem('token')
    setToken('')
    setPage('Home')
    showMessage('Logged out.')
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        {/* Sets clickable logo */}
        <div className="brand" onClick={() => setPage('Home')}>
          <img src={logo} alt="Supply the Spark logo" className="logo" />
        </div>

        <nav className="nav">
        
        {pages
            .filter((item) => {
                {/* Logged out users only see public pages*/}
                if (!token) return ['Home', 'Login', 'Register'].includes(item)

                {/* Logged in users see everything except auth pages*/}
                return !['Login', 'Register'].includes(item)
            })
            .map((item) => (
                <button key={item} onClick={() => setPage(item)} className={page === item ? 'nav-button-active' : 'nav-button'}>
                  {item}
                </button>
                  ))}
                  {token && <button onClick={logout}>Log Out</button>}
        </nav>
      </header>

      {message && <div className="message-box">{message}</div>}

      { page === 'Home' && (
        <section>
            <div className="homepage-hero-image">
                <div className="homepage-logo">
                    <img src={logo} alt="Supply the Spark logo" />
                </div>
            </div>
            <div>
                <p className="mission-info">
                    Supply the Spark is a non-profit organization dedicated to supporting artists and designers
                    through their creative journey by providing them with the supplies they need to share their
                    talent with the world. Artists and designers of all ages deserve the opportunity to share their
                    art, but not everyone has the resources. Supply the Spark provides all kinds of supplies depending
                    on what the artist or designer needs, from colored pencils and sketchbooks to paint and canvases.
                </p>
            </div>
            <div className="homepage-event-info">
                <h1>Upcoming Races</h1>
                <h3>The Creative Stride 5K</h3>
                <p>
                    The next fundraising event for Supple the Spark is the Creative Stride 5K. The race will take place on East
                    Walnut St in Charleston, South Carolina on June 19, 2026. Register for the event now to support local artists
                    and designers!
                </p>
                <button className="register-btn" onClick={() => setPage('Register')}>Register</button>
            </div>
            <div className="image-and-text">
                <div className="homepage-image-wrapper">
                    <img src={homepageRunners} alt="Runners participating in fundraiser" className="homepage-runners-image" />
                </div>
                <div className="homepage-text">
                    <h1>Fundraisers and Events</h1>
                    <p>
                        Supply the Spark hosts a series of races in the Charleston area to support artists and designers
                        in need of essential supplies. Participants can choose from multiple race options, making the event
                        accessible to all experience levels. Each race helps raise funds to provide materials for creatives 
                        who may not have the resources to support their work. From registration to the finish line, every part
                        of the event is focused on giving artists and designers the tools they need to continue creating.
                    </p>
                </div>
            </div>
          </section>
      )}

        {page === 'Login' && (
          <section className="login-panel">
            <h2>Login</h2>
            
            {message && (
                <div className="form-message" role="alert">
                    {message}
                </div>
            )}

            <form onSubmit={handleLogin} className="login-form">

            <div className="form-group">
              <label htmlFor="email">
                Email
                <input
                  id="email"
                  type="email"
                  placeholder="you@example.com"
                  value={loginForm.email}
                  onChange={(e) => setLoginForm({ ...loginForm, email: e.target.value })}
                />
              </label>
            </div>

            <div className="form-group">
              <label htmlFor="password">
                Password
                <input
                  id="password"
                  type="password"
                  placeholder="Enter your password"
                  value={loginForm.password}
                  onChange={(e) => setLoginForm({ ...loginForm, password: e.target.value })}
                />
              </label>
            </div>

              <button type="submit" disabled={isLoading}>
                {isLoading ? 'Logging in...' : 'Log In'}
               </button>

            </form>
          </section>
        )}

        {page === 'Register' && (
          <section className="register-page">
            <div className="register-title">
                <h2>Register</h2>
            </div>

            <div className="register-card">
              
            <form onSubmit={handleRegister} className="register-form">
            {/*
                label for better reading accessibility and placeholder for
                further instruction and clean design
            */}
              <label className="form-field">
                Name
                <input placeholder="Enter your full name"
                  value={registerForm.name}
                  onChange={(e) => setRegisterForm({ ...registerForm, name: e.target.value })}
                />
              </label>

            <div className="form-row">

              <label className="form-field">
                Email
                <input placeholder="Enter your email address"
                  type="email"
                  value={registerForm.email}
                  onChange={(e) => setRegisterForm({ ...registerForm, email: e.target.value })}
                />
              </label>

              <label className="form-field small-field">
                Age
                <input placeholder="Enter your age"
                  value={registerForm.age}
                  onChange={(e) => setRegisterForm({ ...registerForm, age: e.target.value })}
                />
              </label>

            </div>

            <div className="form-row">

              <label className="form-field">
                Emergency Phone
                <input placeholder="Enter your emergency phone"
                  value={registerForm.emergencyPhone}
                  onChange={(e) => setRegisterForm({ ...registerForm, emergencyPhone: e.target.value })}
                />
              </label>

              
              
              <label className="form-field">
                Team Name (optional)
                <input placeholder="Enter your team name"
                  value={registerForm.teamName}
                  onChange={(e) => setRegisterForm({ ...registerForm, teamName: e.target.value })}
                />
              </label>

            </div>
                <label className="form-field">
                    Password
                    <input placeholder="Enter your password"
                        type="password"
                        value={registerForm.password}
                        onChange={(e) => setRegisterForm({ ...registerForm, password: e.target.value })}
                    />
                </label>
                    <label className="form-field">
                        Confirm password
                        <input placeholder="Confirm your password"
                        type="password"
                        value={registerForm.confirmPassword}
                            onChange={(e) => setRegisterForm({...registerForm, confirmPassword: e.target.value})}
                        />
                    </label>

              {message && <p className="form-message">{message}</p>}
              <div className="submit-row">
                 <button type="submit" className="submit-btn">Submit</button>
              </div>

            </form>
            </div>
          </section>
        )}

        {page === 'Dashboard' && (
          <section className="dashboard-container">
            {/* Hero */}
            <div className="hero"
                    style={{ backgroundImage: `url(${runnerBg})` }}
                >
                <div className="hero-overlay">
                    <h1>Dashboard</h1>
                </div>
            </div>
           
            {/* Main content */}
            <div className="event-info">

            {/* Event title */}
            <div className="event-title">
                <h2>The Creative Stride 5K</h2>
            </div>
            <div className="event-subtitle">
                <p>East Walnut St, Charleston, SC</p>
            </div>

            {/* Schedule */}
            <h3>Schedule</h3>

            <div className="schedule-container">

                <div className="schedule-column">
                    <div className="schedule-card">June 1<br />Registration opens</div>
                    <div className="schedule-card">June 19 - 11 AM<br />Check-in</div>
                    <div className="schedule-card">June 19 - 12 PM<br />Race start</div>
                    <div className="schedule-card">June 19 - 1PM<br />Race end</div>
                </div>

                <div className="schedule-column">
                    <div className="schedule-card">June 19 - 1:30 PM<br />Live Art Showcase</div>
                    <div className="schedule-card">June 19 - 3 PM<br />Awards & Artist Spotlight</div>
                </div>

            </div>

            {/* Teammates */}
            <h3>Team</h3>

               <div className="teammates-container">
                  {teammates && teammates.length > 0 ? (
                    teammates.map((mate, index) => (
                      <div key={index} className="teammate-card">
                        {mate.name || `Teammate ${index + 1}`}
                      </div>
                    ))
                  ) : (
                    <p>No teammates yet. Join or create a team!</p>
                  )}
                </div>
            
            {/* Map */}
            <div className="map-section">
                <img src={mapImage} alt="Race Map" 
                     onClick={() => setShowMap(true)}
                />
            </div>
            </div>
           </section>
        )}

        {page === 'Admin' && (
                <section className="admin-panel">
                <h2>Admin Dashboard</h2>

                {!profile?.isAdmin ? (
                    <p>You do not have access to this page.</p>
                ) : (
                    <div>
                    <div className="admin-table-wrapper">
                        <table className="admin-table">
                        <thead>
                            <tr>
                            <th>Name</th>
                            <th>Email</th>
                            <th>Team</th>
                            <th>Age</th>
                            <th>Role</th>
                            <th>Action</th>
                            </tr>
                        </thead>

                        <tbody>
                            {adminRunners.map((runner) => (
                            <tr key={runner.id}>
                                <td>{runner.name}</td>
                                <td>{runner.email}</td>
                                <td>{runner.teamName || '-'}</td>
                                <td>{runner.age}</td>

                                <td>
                                <span className={runner.isAdmin ? "badge gold" : "badge blue"}>
                                    {runner.isAdmin ? "Admin" : "User"}
                                </span>
                                </td>

                                <td>
                                {runner.isAdmin ? (
                                    <button className="admin-btn danger" onClick={() => removeAdmin(runner.id)}>
                                    Remove Admin
                                    </button>
                                ) : (
                                    <button className="admin-btn" onClick={() => makeAdmin(runner.id)}>
                                    Make Admin
                                    </button>
                                )}
                                </td>
                            </tr>
                            ))}
                        </tbody>
                        </table>
                    </div>
                    {/* mobile admin layout */}
                    <div className="admin-cards">
                        {adminRunners.map((runner) => (
                        <div key={runner.id} className="admin-card">
                            <div>Name: {runner.name}</div>
                            <div>Email: {runner.email}</div>
                            <div>Team: {runner.teamName || '-'}</div>
                            <div>Age: {runner.age}</div>

                            <div>
                            Role: {" "}
                            <span className={runner.isAdmin ? "badge gold" : "badge blue"}>
                                {runner.isAdmin ? "Admin" : "User"}
                            </span>
                            </div>

                            <div className="card-actions">
                            {runner.isAdmin ? (
                                <button className="admin-btn danger" onClick={() => removeAdmin(runner.id)}>
                                Remove Admin
                                </button>
                            ) : (
                                <button className="admin-btn" onClick={() => makeAdmin(runner.id)}>
                                Make Admin
                                </button>
                            )}
                            </div>
                        </div>
                        ))}
                    </div>
                    </div>
                )}
                </section>
            )}
            {/* Map modal */}
            {showMap && (
                <div className="map-modal" onClick={() => setShowMap(false)}>
                    <img src={mapImage} alt="Race Map Enlarged" />
                </div>
            )}
    </div>
    )
}